using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;

namespace GameLauncher
{
    public partial class MainWindow : Window
    {
        private bool isFullscreen = false;
        private WindowState previousWindowState;
        private ResizeMode previousResizeMode;
        private bool previousTopmost;
        private bool isSidebarVisible = true;
        private GridLength lastSidebarWidth = new GridLength(320);
        private bool wasSidebarVisibleBeforeFullscreen = true;
        private Dictionary<string, List<Game>> gameCategories = new Dictionary<string, List<Game>>();
        private Game currentSelectedGame;

        public MainWindow()
        {
            InitializeComponent();
            InitializeWebView();
            LoadGames();
            StateChanged += (_, __) => UpdateMaximizeIcon();
            UpdateMaximizeIcon();
            Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string updatesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Updates");
                    if (Directory.Exists(updatesPath)) return;
                    bool userConfirmed = ShowFirstPrompt();
                    if (userConfirmed)
                    {
                        CheckAdmin();
                        EnsureWebView2Installed();
                        Directory.CreateDirectory(updatesPath);
                    }
                    else
                    {
                        Close();
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }
        static bool IsWebView2Available(out string version)
        {
            try
            {
                version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                version = null;
                return false;
            }
            catch (Exception ex)
            {
                version = ex.Message;
                return false;
            }
        }

        public static int EnsureWebView2Installed()
        {
            if (IsWebView2Available(out var ver))
            {
                return 0;
            }

            MessageBox.Show("WebView2 runtime not found. Downloading bootstrapper...", "WebView2", MessageBoxButton.OK, MessageBoxImage.Information);

            var downloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            var tempPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");

            try
            {
                using (var wc = new WebClient())
                {
                    wc.DownloadFile(downloadUrl, tempPath);
                }

                MessageBox.Show($"Downloaded bootstrapper to:\n{tempPath}", "WebView2", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Download failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return 2;
            }

            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true,
                Verb = "runas",
            };

            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        MessageBox.Show("Failed to start installer process.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return 3;
                    }

                    p.WaitForExit();
                    MessageBox.Show($"Installer exited with code {p.ExitCode}", "WebView2 Installer", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installer start failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return 4;
            }

            if (IsWebView2Available(out ver))
            {
                MessageBox.Show($"WebView2 runtime installed: {ver}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }
            else
            {
                MessageBox.Show("WebView2 still unavailable after running installer.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return 5;
            }
        }

        private void CheckAdmin()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Assembly.GetEntryAssembly().CodeBase,
                    Verb = "runas"
                };
                try
                {
                    Process.Start(startInfo);
                    Environment.Exit(0);
                }
                catch
                {
                    Environment.Exit(0);
                }
            }
        }

        public static bool ShowFirstPrompt()
        {
            var window = new Window
            {
                Width = 650,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
            };

            var mainPanel = new StackPanel
            {
                Margin = new Thickness(25),
                VerticalAlignment = VerticalAlignment.Center
            };

            var headerText = new TextBlock
            {
                Text = "Thanks for using this launcher!\n\nThis project is maintained for free in our spare time, so if you'd like to support us, donations are welcome:",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var btcPanel = CreateAddressPanel("• Bitcoin:", "bc1q6lsh23zk52xc346vwznpc74qv038x9glh600c5");
            var xmrPanel = CreateAddressPanel("• Ethereum (ETH):", "0x1365ac03EcbA64fE20b83B029a03A2aF100a61F7");
            var ltcPanel = CreateAddressPanel("• Litecoin (LTC):", "LS8ErpqDXqroquyV1VonMbxn3K5XXDaz73");

            var footerText = new TextBlock
            {
                Text = "This launcher includes several other open-source projects. Feel free to star them on GitHub! (right-click a game to find its repo, if available)\n\nTo ensure that WebView is installed during the first launch (and to install it if it is not) the program must be run (only this time!) with administrator privileges.\n\nPlease confirm by typing \"Yes\" (this confirmation is only required the first time you open the launcher). Thanks!",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 15, 0, 20)
            };

            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var textBox = new TextBox
            {
                Width = 150,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100))
            };

            bool result = false;
            var confirmButton = new Button
            {
                Content = "Confirm",
                Width = 80,
                Height = 25,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100))
            };

            confirmButton.Click += (s, e) =>
            {
                result = string.Equals(textBox.Text.Trim(), "Yes", System.StringComparison.OrdinalIgnoreCase);
                window.Close();
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    result = string.Equals(textBox.Text.Trim(), "Yes", System.StringComparison.OrdinalIgnoreCase);
                    window.Close();
                }
            };

            inputPanel.Children.Add(textBox);
            inputPanel.Children.Add(confirmButton);

            mainPanel.Children.Add(headerText);
            mainPanel.Children.Add(btcPanel);
            mainPanel.Children.Add(xmrPanel);
            mainPanel.Children.Add(ltcPanel);
            mainPanel.Children.Add(footerText);
            mainPanel.Children.Add(inputPanel);

            window.Content = mainPanel;
            textBox.Focus();
            window.ShowDialog();

            return result;
        }

        private static StackPanel CreateAddressPanel(string label, string address)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 5, 0, 5)
            };

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center
            };

            var addressBox = new TextBox
            {
                Text = address,
                IsReadOnly = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)),
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Width = 450
            };

            panel.Children.Add(labelText);
            panel.Children.Add(addressBox);

            return panel;
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;

            UpdateMaximizeIcon();
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdateMaximizeIcon()
        {
            if (MaxRestoreButton != null)
            {
                MaxRestoreButton.Content = (WindowState == WindowState.Maximized) ? "\uE923" : "\uE922";
            }
        }

        private async void InitializeWebView()
        {
            await GameWebView.EnsureCoreWebView2Async();

            GameWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            GameWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        }

        private void CoreWebView2_AcceleratorKeyPressed(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            const int VK_F11 = 0x7A;
            const int VK_ESCAPE = 0x1B;

            if (e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown)
            {
                if (e.VirtualKey == VK_F11)
                {
                    Dispatcher.Invoke(ToggleFullscreen);
                    e.Handled = true;
                }
                else if (e.VirtualKey == VK_ESCAPE && isFullscreen)
                {
                    Dispatcher.Invoke(ToggleFullscreen);
                    e.Handled = true;
                }
            }
        }

        private void LoadGames()
        {
            gameCategories["RACING GAMES"] = new List<Game>
            {
                new Game { Name = "HexGL", Description = "Futuristic racing game", Url = "https://hexgl.bkcore.com/play/", GithubUrl = "https://github.com/BKcore/HexGL" },
                new Game { Name = "Trigger Rally", Description = "Fast arcade rally racing", Url = "https://codeartemis.github.io/TriggerRally/server/public/", GithubUrl = "https://github.com/CodeArtemis/TriggerRally" },
                new Game { Name = "Slow Roads", Description = "Racing game", Url = "https://slowroads.io" },
                new Game { Name = "Montblanc Legend Red", Description = "Racing game", Url = "https://therace.montblanclegend.com/" },
                new Game { Name = "Montblanc Explorer Platinum", Description = "Racing game", Url = "https://therace.montblancexplorer.com/" },
                new Game { Name = "Across The Multiverse", Description = "Racing game", Url = "https://across-multiverse.com/" }
            };

            gameCategories["SHOOTER"] = new List<Game>
            {
                new Game { Name = "Kour.io", Description = "PvP FPS", Url = "https://kour.io" },
                new Game { Name = "ShellShock", Description = "PvP FPS", Url = "https://shellshock.io/" },
                new Game { Name = "Krunker.io", Description = "PvP FPS", Url = "https://krunker.io" },
                new Game { Name = "Venge", Description = "PvP FPS", Url = "https://venge.io" },
                new Game { Name = "LolShot", Description = "PvP FPS", Url = "https://lolshot.io/" },
            };

            gameCategories["BOARD GAMES"] = new List<Game>
            {
                new Game { Name = "c4", Description = "Connect Four game, with AI", Url = "https://kenrick95.github.io/c4/", GithubUrl = "https://github.com/kenrick95/c4" },
                new Game { Name = "Green Mahjong", Description = "Solitaire mahjong game", Url = "http://greenmahjong.daniel-beck.org/#start", GithubUrl = "https://github.com/danbeck/green-mahjong" },
                new Game { Name = "Lichess", Description = "Free chess game", Url = "http://lichess.org/", GithubUrl = "https://github.com/ornicar/lila" }
            };

            gameCategories["ARCADE"] = new List<Game>
            {
                new Game { Name = "Alge's Escapade", Description = "Arcade game where you control an Algae.", Url = "https://dave-and-mike.github.io/game-off-2012/", GithubUrl = "https://github.com/Dave-and-Mike/game-off-2012" },
                new Game { Name = "Avabranch", Description = "Simple arcade game", Url = "https://avabranch.zolmeister.com/", GithubUrl = "https://github.com/Zolmeister/avabranch" },
                new Game { Name = "Ball And Wall", Description = "Arkanoid style game", Url = "https://ballandwall.com/", GithubUrl = "https://github.com/budnix/ball-and-wall" },
                new Game { Name = "Captain Rogers", Description = "Asteroid Belt of Sirius", Url = "https://rogers.enclavegames.com/", GithubUrl = "https://github.com/EnclaveGames/Captain-Rogers" },
                new Game { Name = "Circus Charly", Description = "Tribute in phaser", Url = "https://gamegur-us.github.io/circushtml5/", GithubUrl = "https://github.com/Gamegur-us/circushtml5" },
                new Game { Name = "Coil", Description = "Defeat enemies by wrapping them in your trail", Url = "https://lab.hakim.se/coil/", GithubUrl = "https://github.com/leereilly/Coil" },
                new Game { Name = "Custom Tetris", Description = "Modular Tetris with configurable sides and multiplayer", Url = "https://ondras.github.io/custom-tetris/", GithubUrl = "https://github.com/ondras/custom-tetris" },
                new Game { Name = "Drill Bunny", Description = "Simple arcade game", Url = "https://dreamshowadventures.github.io/LudumDare29/", GithubUrl = "https://github.com/DreamShowAdventures/LudumDare29" },
                new Game { Name = "hurry!", Description = "A small but speedy arcade shooter", Url = "https://hughsk.io/ludum-dare-27/", GithubUrl = "https://github.com/hughsk/ludum-dare-27" },
                new Game { Name = "Sorades 13k", Description = "Scrolling shooter (Raptor / Warning Forever style)", Url = "http://maettig.com/code/canvas/starship-sorades-13k/", GithubUrl = "https://github.com/maettig/starship-sorades-13k" },
            };

            gameCategories["PUZZLES"] = new List<Game>
            {
                new Game { Name = "Maze3D", Description = "A 3D Maze game", Url = "https://demonixis.github.io/Maze3D/", GithubUrl = "https://github.com/demonixis/Maze3D" },
                new Game { Name = "Hex 2048", Description = "Hexgrid-based clone of 2048", Url = "https://jeffhou.github.io/hex-2048/", GithubUrl = "https://github.com/jeffhou/hex-2048" },
                new Game { Name = "The House", Description = "Simple adventure game", Url = "https://the-house.arturkot.pl/", GithubUrl = "https://github.com/arturkot/the-house-game" },
            };

            CategoriesControl.ItemsSource = gameCategories;
        }

        private void GameItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Game game)
            {
                LoadGame(game);
            }
        }

        private void LoadGame(Game game)
        {
            if (currentSelectedGame != null)
                currentSelectedGame.IsSelected = false;

            game.IsSelected = true;
            currentSelectedGame = game;

            WelcomeText.Visibility = Visibility.Collapsed;
            GameWebView.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            GameWebView.Source = new Uri(game.Url);

            GameWebView.Focus();
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            SetSidebarVisibility(!isSidebarVisible);
        }

        private void SetSidebarVisibility(bool visible)
        {
            if (visible)
            {
                if (lastSidebarWidth.Value <= 0)
                    lastSidebarWidth = new GridLength(320);

                SidebarColumn.Width = lastSidebarWidth;
                SplitterColumn.Width = new GridLength(6);
                GameListPanel.Visibility = Visibility.Visible;
                ColumnSplitter.Visibility = Visibility.Visible;
            }
            else
            {
                if (isSidebarVisible && SidebarColumn.Width.Value > 0)
                    lastSidebarWidth = SidebarColumn.Width;

                SidebarColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                GameListPanel.Visibility = Visibility.Collapsed;
                ColumnSplitter.Visibility = Visibility.Collapsed;
            }
            isSidebarVisible = visible;
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            if (!isFullscreen)
            {
                EnterFullscreen();
            }
            else
            {
                ExitFullscreen();
            }
        }

        private void EnterFullscreen()
        {
            if (isFullscreen) return;

            wasSidebarVisibleBeforeFullscreen = isSidebarVisible;

            TopBar.Visibility = Visibility.Collapsed;
            WindowChrome.SetWindowChrome(this, null);

            if (isSidebarVisible)
            {
                SetSidebarVisibility(false);
            }
            else
            {
                SplitterColumn.Width = new GridLength(0);
                ColumnSplitter.Visibility = Visibility.Collapsed;
            }

            previousWindowState = WindowState;
            previousResizeMode = ResizeMode;
            previousTopmost = Topmost;

            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;

            isFullscreen = true;
            FullscreenButton.Content = "Exit Fullscreen (F11)";
        }

        private void ExitFullscreen()
        {
            if (!isFullscreen) return;

            var chrome = new WindowChrome
            {
                CaptionHeight = 50,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            ResizeMode = previousResizeMode;
            Topmost = previousTopmost;
            WindowState = previousWindowState;

            TopBar.Visibility = Visibility.Visible;
            SetSidebarVisibility(wasSidebarVisibleBeforeFullscreen);

            isFullscreen = false;
            FullscreenButton.Content = "Fullscreen (F11)";
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (currentSelectedGame != null)
            {
                currentSelectedGame.IsSelected = false;
                currentSelectedGame = null;
            }

            GameWebView.Visibility = Visibility.Collapsed;
            WelcomeText.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;

            if (GameWebView.CoreWebView2 != null)
            {
                GameWebView.CoreWebView2.Navigate("about:blank");
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && isFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
        }

        private void CopySiteLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                Clipboard.SetText(url);
            }
        }

        private void CopyGithubLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                Clipboard.SetText(url);
            }
        }
    }

    public class Game : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string GithubUrl { get; set; }
        public bool HasGithub => !string.IsNullOrWhiteSpace(GithubUrl);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}