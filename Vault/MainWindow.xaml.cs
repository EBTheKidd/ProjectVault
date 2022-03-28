using HandyControl.Themes;
using HandyControl.Tools;
using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Vault
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    public partial class MainWindow : HandyControl.Controls.Window
    {
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetPhysicallyInstalledSystemMemory(out long TotalMemoryInKilobytes);

        private WaveOutEvent outputDevice;
        private AudioFileReader audioFile;
        private BeatDataModel selectedBeat;
        private SampleDataModel selectedSample;
        private BeatDataModel editBeat;
        public TagLib.IPicture artwork { get; set; }
        public static ObservableCollection<BeatDataModel> BeatDatas { get; set; } = new ObservableCollection<BeatDataModel>();
        public static ObservableCollection<SampleDataModel> SampleDatas { get; set; } = new ObservableCollection<SampleDataModel>();
        public MainWindow()
        {
            // Init window & components
            var chrome = new WindowChrome
            {
                CornerRadius = new CornerRadius(),
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                NonClientFrameEdges = NonClientFrameEdges.None,
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);
            InitializeComponent();
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
            System.Windows.Application.Current.MainWindow?.OnApplyTemplate();
            if (OSVersionHelper.IsWindows11_OrGreater) // apply mica for windows 11 :)
            {
                this.Background = Brushes.Transparent;
                ThemeResources.Current.UsingSystemTheme = true;
                MicaHelper.ApplyMicaEffect(System.Windows.Application.Current.MainWindow.GetHandle(), true);
            }

            // load initial data (libraries, etc...)
            string[] beatFolders = Properties.Settings.Default.beatLibraryPath.Split(',');
            foreach (string s in beatFolders)
            {
                beatFoldersList.Items.Add(new ListBoxItem()
                {
                    Content = s
                });
            }
            string[] sampleFolders = Properties.Settings.Default.sampleLibraryPath.Split(',');
            foreach (string s in sampleFolders)
            {
                sampleFoldersList.Items.Add(new ListBoxItem()
                {
                    Content = s
                });
            }
            Task.Run(async () => getBeats());
            Task.Run(async () => getSamples());

            // Start data monitors (ram, cpu, now playing...)
            PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            System.Windows.Threading.DispatcherTimer dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(delegate
            {
                // resources
                long memKb;
                long memUsed = (GC.GetTotalMemory(false) / 1024 / 1024);
                GetPhysicallyInstalledSystemMemory(out memKb);
                memKb = (memKb / 1024);
                ramUsageLabel.Content = "Ram Used: (" + memUsed + " MB /" + memKb + " MB)";
                ramUsage.Value = int.Parse(memUsed + "");
                ramUsage.Maximum = memKb;
                var cpuUsed = cpuCounter.NextValue();
                cpuUsageLabel.Content = "CPU Used: " + Math.Round((cpuUsed / 255) * 100, 5) + "%";
                cpuUsage.Value = cpuUsed;
                cpuUsage.Maximum = 255;

                // now playing
                string foundBeatTitle = "";
                string foundSampleTitle = "";
                try
                {
                    foundBeatTitle = BeatDatas.Where(x => x.filePath.Equals(audioFile?.FileName)).FirstOrDefault().Title;
                    if (foundBeatTitle.Length > 0)
                    {
                        nowPlayingTitle.Content = foundBeatTitle;
                    }
                }
                catch (Exception ee) { }
                try
                {
                    foundSampleTitle = SampleDatas.Where(x => x.filePath.Equals(audioFile?.FileName)).FirstOrDefault().Title;
                    if (foundSampleTitle.Length > 0)
                    {
                        nowPlayingTitle.Content = foundSampleTitle;
                    }
                }
                catch (Exception ee) { }
                if (outputDevice?.PlaybackState == PlaybackState.Playing)
                {
                    nowPlayingSlider.Value = double.Parse(audioFile?.Position + "");
                    nowPlayingSlider.Maximum = double.Parse(audioFile?.Length + "");

                    nowPlayingPlayBtn.SolidIcon = Meziantou.WpfFontAwesome.FontAwesomeSolidIcon.PauseCircle;
                }
                else
                {
                    nowPlayingPlayBtn.SolidIcon = Meziantou.WpfFontAwesome.FontAwesomeSolidIcon.PlayCircle;
                }
            });
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
            welcomeGrid.Visibility = Visibility.Visible;
        }
        public struct BeatDataModel
        {
            [Category("Metadata")]
            public string Title { get; set; }
            [Category("Metadata")]
            public string Artist { get; set; }
            [Category("Metadata")]
            public string Album { get; set; }
            [Category("Metadata"), ReadOnly(true)]
            public string filePath { get; set; }

            [Category("Metadata"), ReadOnly(true)]
            public double BPM { get; set; }
            [Category("Metadata"), ReadOnly(true)]
            public string Bitrate { get; set; }
            [Category("Metadata"), ReadOnly(true)]
            public string Duration { get; set; }
            [Category("Metadata"), ReadOnly(true)]
            public string Description { get; set; }
        }
        public struct SampleDataModel
        {
            public string Title { get; set; }
            public string filePath { get; set; }
        }
        public async void getSamples()
        {
            await Dispatcher.BeginInvoke(new Action(() => {
                sampleLoadingPanel.Visibility = Visibility.Visible;
            }));
            string[] sampleFolders = Properties.Settings.Default.sampleLibraryPath.Split(',');
            foreach (string s in sampleFolders)
            {
                //Grabs all files from FileDirectory
                string[] files;
                files = Directory.GetFiles(s);

                await Dispatcher.BeginInvoke(new Action(() => {
                    sampleLibraryLoadingLabel.Content = "Loading Samples In " + s;
                    sampleLibraryLoading.Maximum = files.Length - 1;
                }));

                //Checks all files and stores all WAV files into the Files list.
                for (int i = 0; i < files.Length; i++)
                {
                    await Dispatcher.BeginInvoke(new Action(() => {
                        sampleLibraryLoading.Value = i;
                    }));
                    if (files[i].EndsWith(".wav") || files[i].EndsWith(".mp3"))
                    {
                        try
                        {
                            SampleDataModel b = new SampleDataModel
                            {
                                Title = System.IO.Path.GetFileNameWithoutExtension(files[i]),
                                filePath = files[i]
                            };
                            await Dispatcher.BeginInvoke(new Action(() => {
                                if (!SampleDatas.Contains(b))
                                {
                                    SampleDatas.Add(b);
                                }
                            }));
                        }
                        catch (Exception ee)
                        {
                            Console.WriteLine("could not load sample " + i);
                        }
                    }
                }
            }
            
            await Dispatcher.BeginInvoke(new Action(() => {
                sampleLoadingPanel.Visibility = Visibility.Collapsed;
            }));
        }
        public async void getBeats()
        {
            await Dispatcher.BeginInvoke(new Action(() => {
                beatLoadingPanel.Visibility = Visibility.Visible;
            }));
            string[] beatFolders = Properties.Settings.Default.beatLibraryPath.Split(',');
            foreach (string s in beatFolders)
            {
                //Grabs all files from FileDirectory
                string[] files;
                files = Directory.GetFiles(s);

                await Dispatcher.BeginInvoke(new Action(() => {
                    beatLibraryLoadingLabel.Content = "Loading Beats In " + s; 
                    beatLibraryLoading.Maximum = files.Length - 1;
                }));
                //Checks all files and stores all WAV files into the Files list.
                for (int i = 0; i < files.Length; i++)
                {
                    await Dispatcher.BeginInvoke(new Action(() => {
                        beatLibraryLoading.Value = i;
                    }));
                    if (files[i].EndsWith(".wav") || files[i].EndsWith(".mp3"))
                    {
                        var tfile = TagLib.File.Create(files[i]);
                        string title = tfile.Tag.Title;
                        if (title == null || title.Length == 0)
                        {
                            title = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                        }

                        string[] artist = new string[1];
                        if (tfile.Tag.AlbumArtists == null)
                        {
                            artist[0] = "N/A";
                        }
                        else
                        {
                            artist = tfile.Tag.AlbumArtists;
                        }
                        BeatDataModel b = new BeatDataModel
                        {
                            Title = title,
                            Artist = String.Join(",", artist),
                            Album = tfile.Tag.Album,
                            filePath = files[i],
                            Bitrate = (string.Format("{0} Kbps", (int)tfile.Properties.AudioBitrate)),
                            Description = (tfile.Properties.Description),
                            BPM = (tfile.Tag.BeatsPerMinute),
                            Duration = (string.Format("{0}:{1:00}", (int)tfile.Properties.Duration.TotalMinutes, tfile.Properties.Duration.Seconds))
                        };

                        await Dispatcher.BeginInvoke(new Action(() => {
                            if (!BeatDatas.Contains(b))
                            {
                                BeatDatas.Add(b);
                            }
                        }));
                    }
                }
            }

            await Dispatcher.BeginInvoke(new Action(() => {
                beatLoadingPanel.Visibility = Visibility.Collapsed;
            }));
        }
        private void sideMenu_SelectionChanged(object sender, HandyControl.Data.FunctionEventArgs<object> e)
        {
            welcomeGrid.Visibility = Visibility.Collapsed;
            beatLibraryGrid.Visibility = Visibility.Collapsed;
            beatPacksGrid.Visibility = Visibility.Collapsed;
            sampleLibraryGrid.Visibility = Visibility.Collapsed;
            sampleFinderGrid.Visibility = Visibility.Collapsed;
            marketingGrid.Visibility = Visibility.Collapsed;
            settingsGrid.Visibility = Visibility.Collapsed;
            if (homeMenu.IsSelected)
            {
                welcomeGrid.Visibility = Visibility.Visible;
            }
            else if (beatLibraryMenu.IsSelected)
            {
                beatLibraryGrid.Visibility = Visibility.Visible;
            }
            else if (beatPacksMenu.IsSelected)
            {
                beatPacksGrid.Visibility = Visibility.Visible;
            }
            else if (sampleLibraryMenu.IsSelected)
            {
                sampleLibraryGrid.Visibility = Visibility.Visible;
            }
            else if (sampleFinderMenu.IsSelected)
            {
                sampleFinderGrid.Visibility = Visibility.Visible;
            }
            else if (marketingMenu.IsSelected)
            {
                marketingGrid.Visibility = Visibility.Visible;
            }
            else if (settingsMenu.IsSelected)
            {
                settingsGrid.Visibility = Visibility.Visible;
            }
        }
        private void nowPlayingBtn_Click(object sender, RoutedEventArgs e)
        {
            audioPlayerDrawer.IsOpen = !audioPlayerDrawer.IsOpen;
        }

        private void openFileFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (beatLibraryMenu.IsSelected)
            {
                if (beatDataTable.SelectedItem != null)
                {
                    var beat = (BeatDataModel)beatDataTable.SelectedItem;
                    string argument = "/select, \"" + beat.filePath + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                }
            }
            else if (sampleLibraryMenu.IsSelected)
            {
                if (sampleDataTable.SelectedItem != null)
                {
                    var sample = (SampleDataModel)sampleDataTable.SelectedItem;
                    string argument = "/select, \"" + sample.filePath + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                }
            }
        }
        private void playBtn_Click(object sender, RoutedEventArgs e)
        {
            if (outputDevice == null || audioFile == null)
            {
                string playFilePath = "";
                outputDevice = new WaveOutEvent();
                if (beatLibraryMenu.IsSelected)
                {
                    if (beatDataTable.SelectedItem == null)
                    {
                        beatDataTable.SelectedIndex = 0;
                    }
                    playFilePath = ((BeatDataModel)beatDataTable.SelectedItem).filePath;
                }
                else if (sampleLibraryMenu.IsSelected)
                {
                    if (sampleDataTable.SelectedItem == null)
                    {
                        sampleDataTable.SelectedIndex = 0;
                    }
                    playFilePath = ((SampleDataModel)sampleDataTable.SelectedItem).filePath;
                }
                if (playFilePath.Length > 0)
                {
                    audioFile = new AudioFileReader(playFilePath);
                    outputDevice.Init(audioFile);
                }
            } else // switching play file
            {
                string playFilePath = audioFile?.FileName;
                bool beatFile = playFilePath.Equals(BeatDatas.Where(x => x.filePath.Equals(playFilePath)).FirstOrDefault().filePath);
                bool sampleFile = playFilePath.Equals(SampleDatas.Where(x => x.filePath.Equals(playFilePath)).FirstOrDefault().filePath);

                outputDevice?.Dispose();
                outputDevice = null;
                audioFile?.Dispose();
                audioFile = null;
                outputDevice = new WaveOutEvent();
                if (beatLibraryMenu.IsSelected)
                {
                    if (beatDataTable.SelectedItem == null)
                    {
                        beatDataTable.SelectedIndex = 0;
                    }
                    playFilePath = ((BeatDataModel)beatDataTable.SelectedItem).filePath;
                }
                else if (sampleLibraryMenu.IsSelected)
                {
                    if (sampleDataTable.SelectedItem == null)
                    {
                        sampleDataTable.SelectedIndex = 0;
                    }
                    playFilePath = ((SampleDataModel)sampleDataTable.SelectedItem).filePath;
                }
                if (playFilePath.Length > 0)
                {
                    audioFile = new AudioFileReader(playFilePath);
                    outputDevice.Init(audioFile);
                }
            }


            if (outputDevice?.PlaybackState == PlaybackState.Playing)
            {
                nowPlayingPlayBtn.SolidIcon = Meziantou.WpfFontAwesome.FontAwesomeSolidIcon.PlayCircle;
                outputDevice.Pause();
            }
            else if (outputDevice?.PlaybackState == PlaybackState.Paused || outputDevice?.PlaybackState == PlaybackState.Stopped)
            {
                nowPlayingPlayBtn.SolidIcon = Meziantou.WpfFontAwesome.FontAwesomeSolidIcon.PauseCircle;
                outputDevice.Play();
            }
        }
        private void nowPlayingFastBackwards_Click(object sender, RoutedEventArgs e)
        {
            try { audioFile?.Skip(-15); } catch (Exception ee) { }
        }
        private void nowPlayingFastForwards_Click(object sender, RoutedEventArgs e)
        {
            try { audioFile?.Skip(15); } catch (Exception ee) { }
        }
        private void nowPlayingBackwards_Click(object sender, RoutedEventArgs e)
        {
            try { audioFile?.Skip(-3); } catch (Exception ee) { }
        }
        private void nowPlayingForwards_Click(object sender, RoutedEventArgs e)
        {
            try { audioFile?.Skip(3); } catch (Exception ee) { }
        }
        #region Beat Library
        private void beatEditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (beatDataTable.SelectedItem != null)
            {
                editBeat = (BeatDataModel)beatDataTable.SelectedItem;
                editProperties.SelectedObject = (BeatDataModel)beatDataTable.SelectedItem;
                editBeatHeader.Text = ((BeatDataModel)editProperties.SelectedObject).Title;
                beatEditDrawer.IsOpen = !beatEditDrawer.IsOpen;
                var tfile = TagLib.File.Create(((BeatDataModel)editProperties.SelectedObject).filePath);
                try
                {
                    var artwork = tfile.Tag.Pictures[0];
                    MemoryStream ms = new MemoryStream(artwork.Data.Data);
                    ms.Seek(0, SeekOrigin.Begin);
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    editBeatImage.Source = bitmap;
                }
                catch (Exception ee)
                {
                    Console.WriteLine("no image");
                }
            }
        }
        private void editBeatBrowseImage_Click(object sender, RoutedEventArgs e)
        {
            // open file dialog   
            Microsoft.Win32.OpenFileDialog open = new Microsoft.Win32.OpenFileDialog();
            // image filters  
            open.Filter = "Image Files(*.jpg; *.jpeg)|*.jpg; *.jpeg";
            if (open.ShowDialog().Value == true)
            {
                // display image in picture box
                var uri = new Uri(open.FileName);
                editBeatImage.Source = new BitmapImage(uri);
                TagLib.Picture picture = new TagLib.Picture(open.FileName);
                TagLib.Id3v2.AttachedPictureFrame albumCoverPictFrame = new TagLib.Id3v2.AttachedPictureFrame(picture);
                albumCoverPictFrame.MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg;
                albumCoverPictFrame.Type = TagLib.PictureType.FrontCover;
                TagLib.IPicture[] pictFrames = new TagLib.IPicture[1];
                pictFrames[0] = (TagLib.IPicture)albumCoverPictFrame;
                var test = (BeatDataModel)editProperties.SelectedObject;
                artwork = (TagLib.IPicture)albumCoverPictFrame;
            }
        }
        private void saveBeatBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (((BeatDataModel)editProperties.SelectedObject).filePath != null)
                {
                    var tfile = TagLib.File.Create(((BeatDataModel)editProperties.SelectedObject).filePath);
                    tfile.Tag.Title = ((BeatDataModel)editProperties.SelectedObject).Title;
                    tfile.Tag.AlbumArtists = ((BeatDataModel)editProperties.SelectedObject).Artist.Split(',');
                    tfile.Tag.Album = ((BeatDataModel)editProperties.SelectedObject).Album;
                    if (artwork != null)
                    {
                        tfile.Tag.Pictures = new TagLib.IPicture[1] { artwork };
                    }
                    tfile.Save();
                    artwork = null;
                    beatEditDrawer.IsOpen = false;
                    string title = tfile.Tag.Title;
                    if (title == null || title.Length == 0)
                    {
                        title = System.IO.Path.GetFileNameWithoutExtension(((BeatDataModel)editProperties.SelectedObject).filePath);
                    }
                    string[] artists = tfile.Tag.AlbumArtists;
                    string album = tfile.Tag.Album;
                    double bpm = tfile.Tag.BeatsPerMinute;
                    string duration = string.Format("{0}:{1:00}", (int)tfile.Properties.Duration.TotalMinutes, tfile.Properties.Duration.Seconds);
                    BeatDataModel b = new BeatDataModel
                    {
                        Title = title,
                        Artist = artists[0],
                        Album = album,
                        filePath = ((BeatDataModel)editProperties.SelectedObject).filePath,
                        Bitrate = (string.Format("{0} Kbps", (int)tfile.Properties.AudioBitrate)),
                        Description = (tfile.Properties.Description),
                        BPM = (tfile.Tag.BeatsPerMinute),
                        Duration = (string.Format("{0}:{1:00}", (int)tfile.Properties.Duration.TotalMinutes, tfile.Properties.Duration.Seconds))
                    };
                    BeatDatas.Remove(editBeat);
                    if (!BeatDatas.Contains(b))
                    {
                        BeatDatas.Add(b);
                        beatDataTable.SelectedItem = b;
                        beatDataTable.ScrollIntoView(b);
                    }
                }
            }
            catch (Exception ee)
            {
                HandyControl.Controls.MessageBox.Show("There was an issue saving the file data.\n\nPlease make sure the file is not loaded/playing and please try again!", "Error Saving File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void beatSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var sb = (HandyControl.Controls.SearchBar)sender;
            if (sb.Text.Length > 0)
            {
                var tmp = BeatDatas.Where(x => x.Title.ToLower().Contains(sb.Text.ToLower()));
                beatDataTable.ItemsSource = tmp;
            }
            else
            {
                beatDataTable.ItemsSource = BeatDatas;
            }
        }
        #endregion
        #region Sample Library
        private void sampleSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var sb = (HandyControl.Controls.SearchBar)sender;
            if (sb.Text.Length > 0)
            {
                var tmp = SampleDatas.Where(x => x.Title.ToLower().Contains(sb.Text.ToLower()));
                sampleDataTable.ItemsSource = tmp;
            }
            else
            {
                sampleDataTable.ItemsSource = SampleDatas;
            }
        }
        #endregion
        #region Settings
        private async void reloadLibraries_Click(object sender, RoutedEventArgs e)
        {
            BeatDatas.Clear();
            SampleDatas.Clear();
            Task.Run(async () => getBeats());
            Task.Run(async () => getSamples());
        }
        private void saveSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Save();
            HandyControl.Controls.MessageBox.Show("Settings have been applied!\nSome settings (Folder Locations) require a restart to apply.", "Settings Applied!");
        }
        private void addSampleFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Console.WriteLine(dialog.SelectedPath);
                sampleFoldersList.Items.Add(new ListBoxItem()
                {
                    Content = dialog.SelectedPath
                });
            }

            string[] sampleFolders = new string[sampleFoldersList.Items.Count];
            for (int i = 0; i < sampleFolders.Length; i++)
            {
                ListBoxItem item = (ListBoxItem)sampleFoldersList.Items[i];
                sampleFolders[i] = item.Content.ToString();
            }
            Properties.Settings.Default.sampleLibraryPath = String.Join(",", sampleFolders);
            Properties.Settings.Default.Save();
        }
        private void removeSampleFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sampleFoldersList.SelectedItem != null)
            {
                sampleFoldersList.Items.Remove(sampleFoldersList.SelectedItem);
                string[] sampleFolders = new string[sampleFoldersList.Items.Count];
                for (int i = 0; i < sampleFolders.Length; i++)
                {
                    ListBoxItem item = (ListBoxItem)sampleFoldersList.Items[i];
                    sampleFolders[i] = item.Content.ToString();
                }
                Properties.Settings.Default.sampleLibraryPath = String.Join(",", sampleFolders);
                Properties.Settings.Default.Save();
            }
        }
        private void addBeatFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Console.WriteLine(dialog.SelectedPath);
                beatFoldersList.Items.Add(new ListBoxItem()
                {
                    Content = dialog.SelectedPath
                });
            }

            string[] beatFolders = new string[beatFoldersList.Items.Count];
            for (int i = 0; i < beatFolders.Length; i++)
            {
                ListBoxItem item = (ListBoxItem)beatFoldersList.Items[i];
                beatFolders[i] = item.Content.ToString();
            }
            Properties.Settings.Default.beatLibraryPath = String.Join(",", beatFolders);
            Properties.Settings.Default.Save();
        }
        private void removeBeatFolder_Click(object sender, RoutedEventArgs e)
        {
            if (beatFoldersList.SelectedItem != null)
            {
                beatFoldersList.Items.Remove(beatFoldersList.SelectedItem);
                string[] beatFolders = new string[beatFoldersList.Items.Count];
                for (int i = 0; i < beatFolders.Length; i++)
                {
                    ListBoxItem item = (ListBoxItem)beatFoldersList.Items[i];
                    beatFolders[i] = item.Content.ToString();
                }
                Properties.Settings.Default.beatLibraryPath = String.Join(",", beatFolders);
                Properties.Settings.Default.Save();
            }
        }

        #endregion

    }
}
