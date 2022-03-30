﻿using HandyControl.Themes;
using HandyControl.Tools;
using Microsoft.Win32;
using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
using System.Xml.Serialization;
using YoutubeExplode;
using YoutubeExplode.Converter;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;

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
        public TagLib.IPicture artwork { get; set; }
        private WaveOutEvent outputDevice;
        private AudioFileReader audioFile;
        private AudioDataModel nowPlayingAudio { get; set; }
        public bool manualAudioSwitch = false;
        private AudioDataModel editAudio;
        private bool repeat;
        private bool shuffle;
        public static ObservableCollection<AudioDataModel> BeatPackBeatDatas { get; set; } = new ObservableCollection<AudioDataModel>();
        public static ObservableCollection<BeatPackModel> BeatPackDatas { get; set; } = new ObservableCollection<BeatPackModel>();
        public static ObservableCollection<AudioDataModel> BeatDatas { get; set; } = new ObservableCollection<AudioDataModel>();
        public static ObservableCollection<AudioDataModel> SampleDatas { get; set; } = new ObservableCollection<AudioDataModel>();
        public static ObservableCollection<YoutubeVideoModel> YoutubeVideoDatas { get; set; } = new ObservableCollection<YoutubeVideoModel>();
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
            updateTheme();
            LoadBeatPacks();
            LoadVideoDatas();

            outputDevice?.Dispose();
            outputDevice = new WaveOutEvent();
            outputDevice.PlaybackStopped += delegate
            {
                playAudioFile();
            };
            // load initial data (libraries, etc...)
            if (Properties.Settings.Default.beatLibraryPath.Length > 5)
            {
                string[] beatFolders = Properties.Settings.Default.beatLibraryPath.Split(',');
                foreach (string s in beatFolders)
                {
                    if (s.Trim().Length > 0)
                    {
                        beatFoldersList.Items.Add(new ListBoxItem()
                        {
                            Content = s
                        });
                    }
                }
                Task.Run(async () => getBeats()); 
            }
            if (Properties.Settings.Default.sampleLibraryPath.Length > 5)
            {
                string[] sampleFolders = Properties.Settings.Default.sampleLibraryPath.Split(',');
                foreach (string s in sampleFolders)
                {
                    if (s.Trim().Length > 0)
                    {
                        sampleFoldersList.Items.Add(new ListBoxItem()
                        {
                            Content = s
                        });
                    }
                }
                Task.Run(async () => getSamples()); 
            }

            // Start data monitors (ram, cpu, now playing...)
            PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            System.Windows.Threading.DispatcherTimer dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(delegate
            {
                // resources
                long memKb;
                long memUsed = GC.GetTotalMemory(false) / 1024 / 1024;
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
                string foundBeatTitle = BeatDatas.Where(x => x.filePath.Equals(audioFile?.FileName)).FirstOrDefault().Title;
                string foundSampleTitle = SampleDatas.Where(x => x.filePath.Equals(audioFile?.FileName)).FirstOrDefault().Title;
                if (foundBeatTitle != null && foundBeatTitle.Length > 0)
                {
                    nowPlayingTitle.Content = foundBeatTitle;
                }
                if (foundSampleTitle != null && foundSampleTitle.Length > 0)
                {
                    nowPlayingTitle.Content = foundSampleTitle;
                }
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
        public struct AudioDataModel
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
        public struct YoutubeVideoModel
        {
            public string Title { get; set; }
            public string id { get; set; }
            public double rating { get; set; }
        }
        public struct BeatPackModel
        {
            public string id { get; set; }
            public string Title { get; set; }
            public double Cost { get; set; }
            public ObservableCollection<AudioDataModel> Beats { get; set; }
            public List<string> EmailRecipients { get; set; }

            public string getRecipientsStr()
            {
                return String.Join(",", EmailRecipients);
            }
            public string getBeatsStr()
            {
                List<string> titles = new List<string>();
                foreach (AudioDataModel a in Beats)
                {
                    titles.Add(a.Title);
                }
                return String.Join(",", titles);
            }
        }
        public static void SaveBeatPacks()
        {
            XmlSerializer xs = new XmlSerializer(BeatPackDatas.GetType());
            using (StreamWriter wr = new StreamWriter("beatpackdata.xml"))
            {
                xs.Serialize(wr, BeatPackDatas);
            }
        }
        public static void LoadBeatPacks()
        {
            if (File.Exists("beatpackdata.xml"))
            {
                XmlSerializer xs = new XmlSerializer(BeatPackDatas.GetType());
                using (StreamReader rd = new StreamReader("beatpackdata.xml"))
                {
                    BeatPackDatas = xs.Deserialize(rd) as ObservableCollection<BeatPackModel>;
                }
            }
        }
        public void SaveVideoDatas()
        {
            XmlSerializer xs = new XmlSerializer(YoutubeVideoDatas.GetType());
            using (StreamWriter wr = new StreamWriter("videodata.xml"))
            {
                xs.Serialize(wr, YoutubeVideoDatas);
            }
        }
        public void LoadVideoDatas()
        {
            if (File.Exists("videodata.xml"))
            {
                XmlSerializer xs = new XmlSerializer(YoutubeVideoDatas.GetType());
                using (StreamReader rd = new StreamReader("videodata.xml"))
                {
                    YoutubeVideoDatas = xs.Deserialize(rd) as ObservableCollection<YoutubeVideoModel>;
                }
                if (YoutubeVideoDatas.Count > 3)
                {
                    ytSampleSelector.SelectedItem = YoutubeVideoDatas[new Random().Next(YoutubeVideoDatas.Count - 1)];
                }
            }
        }

        #region Global
        public void updateTheme()
        {
            accentColorReset.IsEnabled = true;
            accentColorSelector.IsEnabled = true;
            SolidColorBrush mySolidColorBrush = new SolidColorBrush();
            SolidColorBrush mySolidColorBrush2 = new SolidColorBrush();
            if (Properties.Settings.Default.Theme == "Light")
            {
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                mySolidColorBrush.Color = Color.FromArgb(255, 255, 255, 255);
                mySolidColorBrush2.Color = Color.FromArgb(255, 0, 0, 0);
                lightThemeBtn.IsChecked = true;
                this.Background = mySolidColorBrush;
            }
            else if (Properties.Settings.Default.Theme == "Dark")
            {
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                mySolidColorBrush.Color = Color.FromArgb(255, 28, 28, 28);
                mySolidColorBrush2.Color = Color.FromArgb(255, 255, 255, 255);
                darkThemeBtn.IsChecked = true;
                this.Background = mySolidColorBrush;
            }
            
            if(Properties.Settings.Default.Theme == "Mica")
            {
                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                mySolidColorBrush.Color = Color.FromArgb(255, 28, 28, 28);
                mySolidColorBrush2.Color = Color.FromArgb(255, 255, 255, 255);
                if (OSVersionHelper.IsWindows11_OrGreater) // apply mica for windows 11 :)
                {
                    this.Background = Brushes.Transparent;
                    ThemeResources.Current.UsingSystemTheme = true;
                    MicaHelper.ApplyMicaEffect(System.Windows.Application.Current.MainWindow.GetHandle(), true);
                    micaThemeBtn.IsChecked = true;
                    accentColorReset.IsEnabled = false;
                    accentColorSelector.IsEnabled = false;

                } else
                {
                    HandyControl.Controls.Growl.ErrorGlobal("You must be running Windows 11 to enable Mica!");
                    mySolidColorBrush.Color = Color.FromArgb(255, 28, 28, 28);
                    mySolidColorBrush2.Color = Color.FromArgb(255, 255, 255, 255);
                    darkThemeBtn.IsChecked = true;
                    this.Background = mySolidColorBrush;
                }
            }
            System.Windows.Application.Current.MainWindow?.OnApplyTemplate();

            try
            {
                if (!Properties.Settings.Default.AccentColor.IsEmpty && Properties.Settings.Default.Theme != "Mica")
                {
                    System.Windows.Media.Color primary = System.Windows.Media.Color.FromArgb(Properties.Settings.Default.AccentColor.A, Properties.Settings.Default.AccentColor.R, Properties.Settings.Default.AccentColor.G, Properties.Settings.Default.AccentColor.B);
                    SolidColorBrush primaryColorBrush = new SolidColorBrush();
                    primaryColorBrush.Color = primary;
                    System.Windows.Application.Current.Resources["DarkPrimaryBrush"] = primaryColorBrush;
                    System.Windows.Application.Current.Resources["PrimaryBrush"] = primaryColorBrush;
                }
            }
            catch (Exception e) { }
        }
        public async void getSamples()
        {
            await Dispatcher.BeginInvoke(new Action(() => {
                sampleLoadingPanel.Visibility = Visibility.Visible;
            }));
            List<string> sampleFolders = Properties.Settings.Default.sampleLibraryPath.Split(',').Where(x => x.Trim().Length > 0).ToList();
            List<string> sampleFoldersAll = Properties.Settings.Default.sampleLibraryPath.Split(',').Where(x => x.Trim().Length > 0).ToList();
            foreach (string s in sampleFolders)
            {
                foreach (string d in Directory.GetDirectories(s, "*.*", SearchOption.AllDirectories))
                {
                    sampleFoldersAll.Add(d);
                }
            }
            foreach (string s in sampleFoldersAll)
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
                            AudioDataModel b = new AudioDataModel
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
                                if (!SampleDatas.Contains(b))
                                {
                                    SampleDatas.Add(b);
                                }
                            }));
                        }
                        catch (Exception ee) { Console.WriteLine("Failed to load sample '" + files[i] + "'"); }
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
            string[] beatFolders = Properties.Settings.Default.beatLibraryPath.Split(',').Where(x => x.Trim().Length > 0).ToArray();
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
                        AudioDataModel b = new AudioDataModel
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
        private void openFileFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (beatLibraryMenu.IsSelected)
            {
                if (beatDataTable.SelectedItem != null)
                {
                    var beat = (AudioDataModel)beatDataTable.SelectedItem;
                    string argument = "/select, \"" + beat.filePath + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                }
            }
            else if (sampleLibraryMenu.IsSelected)
            {
                if (sampleDataTable.SelectedItem != null)
                {
                    var sample = (AudioDataModel)sampleDataTable.SelectedItem;
                    string argument = "/select, \"" + sample.filePath + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                }
            }
        }

        public void playAudioFile(bool manual = false)
        {
            ObservableCollection<AudioDataModel> source = new ObservableCollection<AudioDataModel>();
            if (BeatDatas.Contains(nowPlayingAudio)) { source = BeatDatas; }
            if (SampleDatas.Contains(nowPlayingAudio)) { source = SampleDatas; }

            if (outputDevice.PlaybackState != PlaybackState.Stopped)
            {
                outputDevice.Stop();
            }
            else
            {
                if (repeatToggle.IsChecked.Value || manual || audioFile?.FileName != nowPlayingAudio.filePath)
                {
                    audioFile = new AudioFileReader(nowPlayingAudio.filePath);
                    outputDevice.Init(audioFile);
                    nowPlayingPlayBtn.SolidIcon = Meziantou.WpfFontAwesome.FontAwesomeSolidIcon.PauseCircle;
                    outputDevice.Play();
                }
                else if (shuffleToggle.IsChecked.Value)
                {
                    AudioDataModel nextAudio = source[new Random().Next(source.Count)];
                    audioFile = new AudioFileReader(nextAudio.filePath);
                    nowPlayingAudio = nextAudio;
                    outputDevice.Init(audioFile);
                    nowPlayingPlayBtn.SolidIcon = Meziantou.WpfFontAwesome.FontAwesomeSolidIcon.PauseCircle;
                    outputDevice.Play();
                }

                try
                {
                    var tfile = TagLib.File.Create(nowPlayingAudio.filePath);
                    foreach (TagLib.IPicture artwork in tfile.Tag.Pictures)
                    {
                        MemoryStream ms = new MemoryStream(artwork.Data.Data);
                        ms.Seek(0, SeekOrigin.Begin);
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        Image img = new Image()
                        {
                            Source = bitmap,
                            Width = 115,
                            Height = 115,
                            Stretch = Stretch.UniformToFill,
                        };
                        nowPlayingArtwork.Items.Add(img);
                        nowPlayingArtwork.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ee)
                {
                    nowPlayingArtwork.Visibility = Visibility.Collapsed;
                }
                nowPlayingBtn.IsEnabled = true;
            }


        }
        private void playBtn_Click(object sender, RoutedEventArgs e)
        {
            AudioDataModel selectedAudioFile = new AudioDataModel { };
            if (audioPlayerDrawer.IsOpen)
            {
                selectedAudioFile = nowPlayingAudio;
            } else if (beatLibraryMenu.IsSelected)
            {
                if (beatDataTable.SelectedItem == null)
                {
                    beatDataTable.SelectedIndex = 0;
                }
                selectedAudioFile = (AudioDataModel)beatDataTable.SelectedItem;
            }
            else if (sampleLibraryMenu.IsSelected)
            {
                if (sampleDataTable.SelectedItem == null)
                {
                    sampleDataTable.SelectedIndex = 0;
                }
                selectedAudioFile = (AudioDataModel)sampleDataTable.SelectedItem;
            }
            
            if (nowPlayingAudio.filePath != selectedAudioFile.filePath || outputDevice?.PlaybackState == PlaybackState.Stopped) // if different song than currently loaded, load it
            {
                nowPlayingAudio = selectedAudioFile;
                playAudioFile(true);
            } else
            {
                if (outputDevice?.PlaybackState == PlaybackState.Playing) // play/pause loaded song
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
        }
        private void saveAudioBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (((AudioDataModel)editProperties.SelectedObject).filePath != null)
                {
                    var tfile = TagLib.File.Create(((AudioDataModel)editProperties.SelectedObject).filePath);
                    tfile.Tag.Title = ((AudioDataModel)editProperties.SelectedObject).Title;
                    tfile.Tag.AlbumArtists = ((AudioDataModel)editProperties.SelectedObject).Artist.Split(',');
                    tfile.Tag.Album = ((AudioDataModel)editProperties.SelectedObject).Album;
                    if (artwork != null)
                    {
                        tfile.Tag.Pictures = new TagLib.IPicture[1] { artwork };
                    }
                    tfile.Save();
                    artwork = null;
                    audioEditDrawer.IsOpen = false;
                    string title = tfile.Tag.Title;
                    if (title == null || title.Length == 0)
                    {
                        title = System.IO.Path.GetFileNameWithoutExtension(((AudioDataModel)editProperties.SelectedObject).filePath);
                    }
                    string[] artists = tfile.Tag.AlbumArtists;
                    string album = tfile.Tag.Album;
                    double bpm = tfile.Tag.BeatsPerMinute;
                    string duration = string.Format("{0}:{1:00}", (int)tfile.Properties.Duration.TotalMinutes, tfile.Properties.Duration.Seconds);
                    AudioDataModel b = new AudioDataModel
                    {
                        Title = title,
                        Artist = artists[0],
                        Album = album,
                        filePath = ((AudioDataModel)editProperties.SelectedObject).filePath,
                        Bitrate = (string.Format("{0} Kbps", (int)tfile.Properties.AudioBitrate)),
                        Description = (tfile.Properties.Description),
                        BPM = (tfile.Tag.BeatsPerMinute),
                        Duration = (string.Format("{0}:{1:00}", (int)tfile.Properties.Duration.TotalMinutes, tfile.Properties.Duration.Seconds))
                    };
                    if (beatLibraryMenu.IsSelected)
                    {
                        BeatDatas.Remove(editAudio);
                        if (!BeatDatas.Contains(b))
                        {
                            BeatDatas.Add(b);
                            beatDataTable.SelectedItem = b;
                            beatDataTable.ScrollIntoView(b);
                        }
                    }
                    else if (sampleLibraryMenu.IsSelected)
                    {
                        SampleDatas.Remove(editAudio);
                        if (!SampleDatas.Contains(b))
                        {
                            SampleDatas.Add(b);
                            sampleDataTable.SelectedItem = b;
                            sampleDataTable.ScrollIntoView(b);
                        }
                    }
                }
            }
            catch (Exception ee)
            {
                HandyControl.Controls.MessageBox.Show("There was an issue saving the file data.\n\nPlease make sure the file is not loaded/playing and please try again!", "Error Saving File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void audioEditBtn_Click(object sender, RoutedEventArgs e)
        {
            if (beatLibraryMenu.IsSelected)
            {
                if (beatDataTable.SelectedItem != null)
                {
                    editAudio = (AudioDataModel)beatDataTable.SelectedItem;
                }
            }
            
            if (sampleLibraryMenu.IsSelected)
            {
                if (sampleDataTable.SelectedItem != null)
                {
                    editAudio = (AudioDataModel)sampleDataTable.SelectedItem;
                }
            }

            editProperties.SelectedObject = editAudio;
            editBeatHeader.Text = editAudio.Title;
            audioEditDrawer.IsOpen = !audioEditDrawer.IsOpen;
            var tfile = TagLib.File.Create(editAudio.filePath);
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
                editBeatImage.Visibility = Visibility.Visible;
            }
            catch (Exception ee){ editBeatImage.Visibility = Visibility.Collapsed; }
        }
        private void audioEditBrowseImage_Click(object sender, RoutedEventArgs e)
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
                var test = (AudioDataModel)editProperties.SelectedObject;
                artwork = (TagLib.IPicture)albumCoverPictFrame;
                editBeatImage.Visibility = Visibility.Visible;
            }
        }
        private void nowPlayingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (audioFile != null && sender != null)
            {
                audioFile.Position = Convert.ToInt64(nowPlayingSlider.Value);
            }
        }
        #endregion
        #region Now Playing 
        private void nowPlayingBtn_Click(object sender, RoutedEventArgs e)
        {
            audioPlayerDrawer.IsOpen = !audioPlayerDrawer.IsOpen;
        }
        private void nowPlayingVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (outputDevice == null || audioFile == null)
            {

            }
            else
            {
                outputDevice.Volume = float.Parse(nowPlayingVolume.Value / 100 + "");
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
        #endregion
        #region Beat Library
        private void addToBeatPack_Click(object sender, RoutedEventArgs e)
        {
            if (beatDataTable.SelectedItem != null && !BeatPackBeatDatas.Contains((AudioDataModel)beatDataTable.SelectedItem)){
                BeatPackBeatDatas.Add((AudioDataModel)beatDataTable.SelectedItem);
                HandyControl.Controls.Growl.InfoGlobal("'" + ((AudioDataModel)beatDataTable.SelectedItem).Title + "' added to current beat pack!");
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
        #region Beat Packs
        


        private void editBeatPack_Click(object sender, RoutedEventArgs e)
        {
            if (beatPackDataTable.SelectedItem != null)
            {
                var bp = (BeatPackModel)beatPackDataTable.SelectedItem;
                beatPackTitle.Text = bp.Title;
                beatPackCost.Value = bp.Cost;
                BeatPackBeatDatas.Clear();
                foreach (AudioDataModel m in bp.Beats)
                {
                    BeatPackBeatDatas.Add(m);
                }
                beatPackRecipientsList.Items.Clear();
                foreach (string s in bp.EmailRecipients)
                {
                    beatPackRecipientsList.Items.Add(new ListBoxItem()
                    {
                        Content = s
                    });
                }
                createEditBeatPacksTab.IsSelected = true;
            }
        }

        private void deleteBeatPack_Click(object sender, RoutedEventArgs e)
        {
            if (beatPackDataTable.SelectedItem != null)
            {
                var bp = (BeatPackModel)beatPackDataTable.SelectedItem;
                BeatPackDatas.Remove(bp);
                SaveBeatPacks();
            }
        }


        private void saveBeatPack_Click(object sender, RoutedEventArgs e)
        {
            List<string> recipients = new List<string>();
            foreach (ListBoxItem s in beatPackRecipientsList.Items)
            {
                recipients.Add(s.Content.ToString());
            }
            BeatPackModel bpm = new BeatPackModel
            {
                id = Guid.NewGuid().ToString(),
                Title = beatPackTitle.Text,
                Cost = beatPackCost.Value,
                Beats = BeatPackBeatDatas,
                EmailRecipients = recipients
            };
            if (!BeatPackDatas.Contains(bpm))
            {
                BeatPackDatas.Add(bpm);
            }
            SaveBeatPacks();
        }
        private void saveBeatPackZip_Click(object sender, RoutedEventArgs e)
        {
            //
        }
        private void sendBeatPack_Click(object sender, RoutedEventArgs e)
        {
            //
        }
        private void addRecipientsFromFile_Click(object sender, RoutedEventArgs e)
        {
            //
        }
        private void removeBeatPackBeat_Click(object sender, RoutedEventArgs e)
        {
            if (beatPackBeatsList.SelectedItem != null)
            {
                BeatPackBeatDatas.Remove((AudioDataModel)beatPackBeatsList.SelectedItem);
            }
        }
        private void removeBeatPackRecipient_Click(object sender, RoutedEventArgs e)
        {
            if (beatPackRecipientsList.SelectedItem != null)
            {
                beatPackRecipientsList.Items.Remove((ListBoxItem)beatPackRecipientsList.SelectedItem);
            }
        }
        private void addEmailRecipient_Click(object sender, RoutedEventArgs e)
        {
            if (IsValidEmail(emailRecipientTextBox.Text))
            {
                beatPackRecipientsList.Items.Add(new ListBoxItem
                {
                    Content = emailRecipientTextBox.Text
                });
                emailRecipientTextBox.Text = "";
            } else
            {
                HandyControl.Controls.Growl.WarningGlobal("Please Enter A Valid Email!");
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
        private void accentColorSelector_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Media.Color primaryOld = System.Windows.Media.Color.FromArgb(Properties.Settings.Default.AccentColor.A, Properties.Settings.Default.AccentColor.R, Properties.Settings.Default.AccentColor.G, Properties.Settings.Default.AccentColor.B);
            Button thisbtn = (Button)sender;
            var picker = SingleOpenHelper.CreateControl<HandyControl.Controls.ColorPicker>();
            var window = new HandyControl.Controls.PopupWindow { PopupElement = picker };
            picker.SelectedColorChanged += delegate {
                Application.Current.Resources["DarkPrimaryBrush"] = picker.SelectedBrush;
                Application.Current.Resources["PrimaryBrush"] = picker.SelectedBrush;
                Application.Current.Resources["DarkAccentBrush"] = picker.SelectedBrush;
                Application.Current.Resources["AccentBrush"] = picker.SelectedBrush;
                Application.Current.Resources["AccentColor"] = primaryOld;

            };
            picker.Confirmed += delegate {
                window.Close();

                System.Drawing.Color primary = System.Drawing.Color.FromArgb(picker.SelectedBrush.Color.A, picker.SelectedBrush.Color.R, picker.SelectedBrush.Color.G, picker.SelectedBrush.Color.B);
                Properties.Settings.Default.AccentColor = primary;
                Properties.Settings.Default.Save();
            };
            picker.Canceled += delegate {
                window.Close();
                System.Drawing.Color defaultColor = System.Drawing.Color.FromArgb(primaryOld.A, primaryOld.R, primaryOld.G, primaryOld.B);
                Properties.Settings.Default.AccentColor = defaultColor;
                Properties.Settings.Default.Save();
                SolidColorBrush primaryColorBrush = new SolidColorBrush();
                primaryColorBrush.Color = primaryOld;
                Application.Current.Resources["DarkPrimaryBrush"] = primaryColorBrush;
                Application.Current.Resources["PrimaryBrush"] = primaryColorBrush;
            };
            window.Show(thisbtn, false);
        }
        private void accentColorReset_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Media.Color defaultColor = System.Windows.Media.Color.FromArgb(255, 50, 108, 243);
            System.Drawing.Color defaultColor2 = System.Drawing.Color.FromArgb(255, 50, 108, 243);
            Properties.Settings.Default.AccentColor = defaultColor2;
            Properties.Settings.Default.Save();
            SolidColorBrush primaryColorBrush = new SolidColorBrush();
            primaryColorBrush.Color = defaultColor;
            Application.Current.Resources["DarkPrimaryBrush"] = primaryColorBrush;
            Application.Current.Resources["PrimaryBrush"] = primaryColorBrush;
        }
        private void themeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (lightThemeBtn.IsChecked.GetValueOrDefault())
            {
                Properties.Settings.Default.Theme = "Light";
            }
            else if (darkThemeBtn.IsChecked.GetValueOrDefault())
            {
                Properties.Settings.Default.Theme = "Dark";
            }
            else if (micaThemeBtn.IsChecked.GetValueOrDefault())
            {
                Properties.Settings.Default.Theme = "Mica";
            }
            Properties.Settings.Default.Save();
            updateTheme();
        }
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

        #region Sample Finder
        
        private async void nextRandomSample_Click(object sender, RoutedEventArgs e)
        {
            try { ytSampleSelector.SelectedIndex += 1; } catch (Exception eee) { }
        }
        private async void previousRandomSample_Click(object sender, RoutedEventArgs e)
        {
            try { ytSampleSelector.SelectedIndex -= 1; } catch (Exception eee) { }
        }
        private async void randomSample_Click(object sender, RoutedEventArgs e)
        {
            try { ytSampleSelector.SelectedIndex = new Random().Next(YoutubeVideoDatas.Count); } catch (Exception eee) { }
        }
        private async void clearRandomSamples_Click(object sender, RoutedEventArgs e)
        {
            YoutubeVideoDatas.Clear();
            sampleNumber.Content = "None";
            SaveVideoDatas();
        }
        private async void removeSelectedYtSample_Click(object sender, RoutedEventArgs e)
        {
            try { YoutubeVideoDatas.RemoveAt(ytSampleSelector.SelectedIndex); SaveVideoDatas(); nextRandomSample_Click(sender, e); } catch (Exception eee) { }
        }
        private async void downloadSelectedYtSample_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ytSampleSelector.SelectedItem != null)
                {
                    loadingVideo.Visibility = Visibility.Visible;
                    var video = (YoutubeVideoModel)ytSampleSelector.SelectedItem;
                    var youtube = new YoutubeClient();
                    if (!Directory.Exists("Downloads"))
                    {
                        Directory.CreateDirectory("Downloads");
                    }
                    await youtube.Videos.DownloadAsync("https://youtube.com/watch?v=" + video.id, "Downloads/" + video.Title + ".mp3", o => o.SetFFmpegPath("ffmpeg.exe"));
                    // https://github.com/ffbinaries/ffbinaries-prebuilt/releases/download/v4.4.1/ffmpeg-4.4.1-win-64.zip
                    loadingVideo.Visibility = Visibility.Hidden;
                    HandyControl.Controls.Growl.SuccessGlobal(video.Title + " has been downloaded!");
                }
            } catch (Exception eee)
            {
                Console.WriteLine(eee.StackTrace);
                HandyControl.Controls.Growl.ErrorGlobal("There was an error downloading your file!");
            }
        }

        private void tagSearchbox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;

            var list = tagSearchbox.Text.Split(' ').ToList();
            foreach (string s in list)
            {
                HandyControl.Controls.Tag t = new HandyControl.Controls.Tag
                {
                    Content = s,
                    ShowCloseButton = true
                };
                if (!tagPanel.Items.Contains(t))
                {
                    tagPanel.Items.Add(t);
                } 
            }
            tagSearchbox.Text = "";
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ytSampleSelector.SelectedItem != null)
            {
                try
                {
                    var sample = (YoutubeVideoModel)ytSampleSelector.SelectedItem;
                    ytBrowser.Source = new Uri("https://www.youtube-nocookie.com/embed/" + sample.id + "?rel=0&amp;showinfo=0");
                    sampleNumber.Content = ytSampleSelector.SelectedIndex + 1 + " of " + YoutubeVideoDatas.Count;
                    ytsampleRate.Value = sample.rating;
                }
                catch (Exception www) { }
            }
        }
        private void ytsampleRate_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (ytSampleSelector.SelectedItem != null)
            {
                try
                {
                    var sample = YoutubeVideoDatas.Where(x => x.id == ((YoutubeVideoModel)ytSampleSelector.SelectedItem).id).First();
                    var sample2 = sample;
                    sample2.rating = ytsampleRate.Value;
                    YoutubeVideoDatas.Remove(sample);
                    YoutubeVideoDatas.Add(sample2);
                    ytSampleSelector.SelectedItem = sample2;
                    SaveVideoDatas();
                }
                catch (Exception www) { }
            }
        }
        private async void getRandomSamples(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Getting Random Video");
            if (tagPanel.Items.Count > 0)
            {
                loadingVideo.Visibility = Visibility.Visible;
                await Task.Delay(10);
                try
                {
                    var count = 50;
                    var API_KEY = "AIzaSyDY75GZ4FTjiIX6C6Lb-UKQ7Cmvk_67zj4";
                    var q = "";
                    foreach (var t in tagPanel.Items)
                    {
                        try
                        {
                            HandyControl.Controls.Tag tag = (HandyControl.Controls.Tag)t;
                            q += tag.Content.ToString().Replace("|", "%7C") + " ";
                        }
                        catch (Exception eee) { }
                    }
                    var url = "https://www.googleapis.com/youtube/v3/search?key=" + API_KEY + "&maxResults=" + count + "&part=snippet&videoCategoryId=10&type=video&q=" + q;
                    string musicUrl = "";
                    bool loadVideo = false;
                    using (WebClient wc = new WebClient())
                    {
                        var json = wc.DownloadString(url);
                        Console.WriteLine("Json length: " + json.Length);
                        dynamic jsonObject = JsonConvert.DeserializeObject(json);
                        var youtube = new YoutubeClient();
                        foreach (var line in jsonObject["items"])
                        {
                            sampleNumber.Content = ytSampleSelector.SelectedIndex + 1 + " of " + YoutubeVideoDatas.Count;
                            Console.WriteLine("loading " + line["id"]["videoId"]);
                            var video = await youtube.Videos.GetAsync("https://youtube.com/watch?v=" + line["id"]["videoId"]);
                            var title = video.Title;
                            var author = video.Author.Title;
                            var duration = video.Duration;
                            var keywords = video.Keywords;
                            var newVid = new YoutubeVideoModel
                            {
                                Title = title,
                                id = video.Id
                            };
                            if (!YoutubeVideoDatas.Contains(newVid))
                            {
                                YoutubeVideoDatas.Add(newVid);
                                musicUrl = video.Id;
                            }
                            if (musicUrl.Length > 0 && !loadVideo)
                            {
                                loadVideo = true;
                                ytSampleSelector.SelectedIndex = YoutubeVideoDatas.IndexOf(newVid);
                            }
                        }
                    }
                    SaveVideoDatas();
                    loadingVideo.Visibility = Visibility.Collapsed;
                }
                catch (Exception e2)
                {
                    loadingVideo.Visibility = Visibility.Collapsed;
                    Console.WriteLine(e2.StackTrace);
                    HandyControl.Controls.Growl.ErrorGlobal("There was an error with the youtube API. This likely means too many requests are being sent. Please try again later.");
                }
            } else
            {
                HandyControl.Controls.Growl.WarningGlobal("Please Enter Some Search Tags!\n\nTip: You can use Youtube API querry parameters!\n('Instrumental|Acoustic' = Instrumental OR Acoustic, '-Jazz' Removes Jazz Results)");
            }
        }
        #endregion
        bool IsValidEmail(string email)
        {
            var trimmedEmail = email.Trim();

            if (trimmedEmail.EndsWith("."))
            {
                return false; // suggested by @TK-421
            }
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == trimmedEmail;
            }
            catch
            {
                return false;
            }
        }

    }
}
