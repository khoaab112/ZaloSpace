using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ZaloSpaceManager.Models;
using ZaloSpaceManager.Services;

namespace ZaloSpaceManager.UI
{
    public class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private ZaloInfo _zaloInfo;
        private List<Profile> _profiles;
        private AppSettings _settings;
        private DispatcherTimer _statusTimer;

        private class CardControls
        {
            public Profile Profile;
            public Border StatusPill;
            public TextBlock TxtStatus;
            public Button BtnStop;
            public Border PlayOverlay;
            public TextBlock TxtPlay;
            public Border AvatarBorder;
            public TextBlock TxtName;
            public bool IsLaunching;
        }

        private Dictionary<string, CardControls> _cardMap = new Dictionary<string, CardControls>();

        // UI Controls
        private TextBlock _txtPathStatus;
        private TextBlock _txtZaloPath;
        private Border _badgeZaloStatus;
        private StackPanel _panelProfiles;
        private Border _emptyStateNotice;
        private StackPanel _actionBtnsPanel;
        private Button _btnAddProfile;
        private Button _btnLaunchAll;
        private Button _btnCloseAll;

        // Storage Stats Widget in Header
        private TextBlock _txtStorageTotal;
        private TextBlock _txtStorageBreakdown;
        private Border _storageWidgetBorder;

        public MainWindow()
        {
            Title = "Zalo Space Manager - Quản Lý Đa Tài Khoản Zalo PC";
            Width = 920;
            Height = 700;
            MinWidth = 800;
            MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(20, 22, 28));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI, Arial");

            SetAppIcon();
            InitData();
            BuildUI();
            RefreshStorageWidgetAsync();
            StartTimer();
        }

        private void SetAppIcon()
        {
            try
            {
                string[] searchPaths = new string[]
                {
                    @"c:\Users\khoaz\Downloads\tesst\ZaloSpace\resources\app_icon.ico",
                    Path.Combine(ProfileService.GetBaseDirectory(), "app_icon.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"ZaloSpace\resources\app_icon.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"resources\app_icon.ico")
                };

                string foundIcon = null;
                foreach (string p in searchPaths)
                {
                    if (File.Exists(p))
                    {
                        foundIcon = p;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(foundIcon))
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(foundIcon, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    this.Icon = bi;

                    string iconToApply = foundIcon;
                    this.Loaded += delegate
                    {
                        try
                        {
                            IntPtr hWnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                            if (hWnd != IntPtr.Zero)
                            {
                                IntPtr hBig = LoadImage(IntPtr.Zero, iconToApply, 1, 48, 48, 0x0010);
                                IntPtr hSmall = LoadImage(IntPtr.Zero, iconToApply, 1, 16, 16, 0x0010);
                                if (hBig != IntPtr.Zero) SendMessage(hWnd, 0x0080, (IntPtr)1, hBig);
                                if (hSmall != IntPtr.Zero) SendMessage(hWnd, 0x0080, (IntPtr)0, hSmall);
                            }
                        }
                        catch { }
                    };
                }
            }
            catch { }
        }

        private void InitData()
        {
            _settings = ProfileService.LoadSettings();
            _profiles = ProfileService.LoadProfiles();

            // Try to load configured path, otherwise auto-detect
            if (!string.IsNullOrEmpty(_settings.ZaloPath) && Directory.Exists(_settings.ZaloPath))
            {
                _zaloInfo = ZaloDetector.ValidatePath(_settings.ZaloPath);
            }

            if (_zaloInfo != null && _zaloInfo.IsValid)
            {
                EnsureDefaultProfileIfEmpty();
            }

            // Regenerate icons to ensure clean corners without fake badges for real notification support
            try
            {
                string iconDir = ProfileService.GetIconsDirectory();
                foreach (Profile p in _profiles)
                {
                    string ico = IconGenerator.GenerateProfileIcon(_zaloInfo != null ? _zaloInfo.ExePath : null, p.AvatarPath, p.Id, iconDir);
                    p.IconPath = ico;
                }
                TaskbarService.ClearAllIconCaches();
                ProfileService.SaveProfiles(_profiles);
            }
            catch { }

            // Synchronize and repair AutoStart registry entries for profiles
            try
            {
                string mgrExe = AutoStartService.GetManagerExecutablePath();
                foreach (Profile p in _profiles)
                {
                    if (p.AutoStart)
                    {
                        AutoStartService.SetProfileAutoStart(p.Id, string.Format("\"{0}\" --launch {1}", mgrExe, p.Id), true);
                    }
                }
            }
            catch { }
        }

        private void EnsureDefaultProfileIfEmpty()
        {
            if (_profiles.Count == 0 && _zaloInfo != null && _zaloInfo.IsValid)
            {
                // "nếu khách hàng đã đăng nhập app zalo trước đó rồi thì sẽ có luôn 1 zalo ở danh sách cho khách hàng"
                string existingUid = _zaloInfo.ExistingUserId;
                Profile defaultP = ProfileService.CreateExistingDefaultProfile(existingUid);
                _profiles.Add(defaultP);
                ProfileService.SaveProfiles(_profiles);
            }
        }

        private void BuildUI()
        {
            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Top Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Zalo Path Card
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Action Toolbar
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Content area
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Footer

            // 0: Header with Soldier Cat Avatar Logo!
            Border headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(26, 29, 38)),
                Padding = new Thickness(24, 14, 24, 14),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 42, 54)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            StackPanel headerStack = new StackPanel { Orientation = Orientation.Horizontal };

            // Soldier Cat Logo in circle
            Border catLogoBorder = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 145, 255)),
                BorderThickness = new Thickness(2),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            try
            {
                string[] catPaths = new string[]
                {
                    @"c:\Users\khoaz\Downloads\tesst\ZaloSpace\resources\app_icon.jpg",
                    Path.Combine(ProfileService.GetBaseDirectory(), "app_icon.jpg"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"ZaloSpace\resources\app_icon.jpg"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"resources\app_icon.jpg")
                };
                string catImgPath = null;
                foreach (string cp in catPaths)
                {
                    if (File.Exists(cp)) { catImgPath = cp; break; }
                }

                if (!string.IsNullOrEmpty(catImgPath))
                {
                    BitmapImage catImg = new BitmapImage();
                    catImg.BeginInit();
                    catImg.UriSource = new Uri(catImgPath);
                    catImg.DecodePixelWidth = 84;
                    catImg.EndInit();
                    catLogoBorder.Background = new ImageBrush(catImg) { Stretch = Stretch.UniformToFill };
                }
            }
            catch { }
            headerStack.Children.Add(catLogoBorder);

            StackPanel titleGroup = new StackPanel();
            TextBlock titleText = new TextBlock
            {
                Text = "ZALO SPACE MANAGER",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 145, 255))
            };
            TextBlock subTitleText = new TextBlock
            {
                Text = "Nhân bản & Quản lý song song nhiều tài khoản Zalo PC độc lập | Đầy đủ cuộc gọi & thông báo",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 160)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            titleGroup.Children.Add(titleText);
            titleGroup.Children.Add(subTitleText);
            headerStack.Children.Add(titleGroup);

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(headerStack, 0);
            headerGrid.Children.Add(headerStack);

            Border storageWidget = CreateStorageWidget();
            Grid.SetColumn(storageWidget, 1);
            headerGrid.Children.Add(storageWidget);

            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);

            // 1: Zalo Path Card
            Border pathCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 31, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 50, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(24, 16, 24, 8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            Grid pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel pathInfo = new StackPanel();
            StackPanel pathHeaderLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            TextBlock lblPath = new TextBlock { Text = "VỊ TRÍ CÀI ĐẶT ZALO PC:", FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 10, 0) };

            _badgeZaloStatus = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            _txtPathStatus = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold };
            _badgeZaloStatus.Child = _txtPathStatus;
            pathHeaderLine.Children.Add(lblPath);
            pathHeaderLine.Children.Add(_badgeZaloStatus);

            _txtZaloPath = new TextBlock
            {
                FontSize = 13,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            pathInfo.Children.Add(pathHeaderLine);
            pathInfo.Children.Add(_txtZaloPath);
            Grid.SetColumn(pathInfo, 0);
            pathGrid.Children.Add(pathInfo);

            StackPanel pathBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button btnScan = CreateButton("🔍 Tự động quét", Color.FromRgb(36, 110, 220), Color.FromRgb(24, 85, 180));
            btnScan.Click += BtnScan_Click;
            btnScan.Margin = new Thickness(8, 0, 4, 0);

            Button btnBrowse = CreateButton("📁 Chọn thủ công", Color.FromRgb(55, 60, 75), Color.FromRgb(45, 50, 65));
            btnBrowse.Click += BtnBrowse_Click;
            btnBrowse.Margin = new Thickness(4, 0, 0, 0);

            pathBtns.Children.Add(btnScan);
            pathBtns.Children.Add(btnBrowse);
            Grid.SetColumn(pathBtns, 1);
            pathGrid.Children.Add(pathBtns);

            pathCard.Child = pathGrid;
            Grid.SetRow(pathCard, 1);
            rootGrid.Children.Add(pathCard);

            // 2: Toolbar (Active only when scanned)
            Border toolBar = new Border { Margin = new Thickness(24, 10, 24, 12) };
            Grid toolGrid = new Grid();
            toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock listTitle = new TextBlock
            {
                Text = "DANH SÁCH",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 185, 200)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(listTitle, 0);
            toolGrid.Children.Add(listTitle);

            _actionBtnsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnAddProfile = CreateButton("+ Thêm Tài Khoản Mới", Color.FromRgb(0, 145, 255), Color.FromRgb(0, 105, 220));
            _btnAddProfile.Click += BtnAddProfile_Click;
            _btnAddProfile.Margin = new Thickness(6, 0, 6, 0);

            _btnLaunchAll = CreateButton("▶ Mở Tất Cả", Color.FromRgb(40, 167, 69), Color.FromRgb(30, 130, 55));
            _btnLaunchAll.Click += BtnLaunchAll_Click;
            _btnLaunchAll.Margin = new Thickness(4, 0, 4, 0);

            _btnCloseAll = CreateButton("⏹ Đóng Tất Cả", Color.FromRgb(220, 53, 69), Color.FromRgb(180, 40, 55));
            _btnCloseAll.Click += BtnCloseAll_Click;
            _btnCloseAll.Margin = new Thickness(4, 0, 0, 0);

            _actionBtnsPanel.Children.Add(_btnAddProfile);
            _actionBtnsPanel.Children.Add(_btnLaunchAll);
            _actionBtnsPanel.Children.Add(_btnCloseAll);
            Grid.SetColumn(_actionBtnsPanel, 1);
            toolGrid.Children.Add(_actionBtnsPanel);

            toolBar.Child = toolGrid;
            Grid.SetRow(toolBar, 2);
            rootGrid.Children.Add(toolBar);

            // 3: Content Area (Empty State Notice OR Profile List)
            Grid contentArea = new Grid { Margin = new Thickness(24, 0, 24, 12) };

            // Empty State Notice: "Hãy chọn app Zalo trên máy để sử dụng"
            _emptyStateNotice = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 27, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 50, 68)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(32),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock emptyIcon = new TextBlock
            {
                Text = "📂",
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            TextBlock emptyTitle = new TextBlock
            {
                Text = "Hãy chọn app Zalo trên máy để sử dụng",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            TextBlock emptySub = new TextBlock
            {
                Text = "Bấm '🔍 Tự động quét' để hệ thống tự tìm Zalo trên máy, hoặc bấm '📁 Chọn thủ công' để chỉ định vị trí Zalo.exe.",
                FontSize = 13,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 500
            };
            emptyStack.Children.Add(emptyIcon);
            emptyStack.Children.Add(emptyTitle);
            emptyStack.Children.Add(emptySub);
            _emptyStateNotice.Child = emptyStack;
            contentArea.Children.Add(_emptyStateNotice);

            // Profile List ScrollViewer
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _panelProfiles = new StackPanel();
            scroll.Content = _panelProfiles;
            contentArea.Children.Add(scroll);

            Grid.SetRow(contentArea, 3);
            rootGrid.Children.Add(contentArea);

            // 4: Footer
            Border footer = new Border
            {
                Padding = new Thickness(24, 8, 24, 12),
                Background = new SolidColorBrush(Color.FromRgb(22, 24, 30))
            };
            TextBlock txtFooter = new TextBlock
            {
                Text = "⚡ Zalo Space Manager - Bản quyền giải pháp phân vùng không gian độc lập cho Zalo PC.",
                FontSize = 11,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            footer.Child = txtFooter;
            Grid.SetRow(footer, 4);
            rootGrid.Children.Add(footer);

            Content = rootGrid;

            UpdatePathStatusUI();
            RenderProfileCards();
        }

        private Button CreateButton(string text, Color normalColor, Color hoverColor)
        {
            Button btn = new Button
            {
                Content = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = null
            };

            // Modern ControlTemplate with 8px CornerRadius and smooth hover effect
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(normalColor));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(14, 8, 14, 8));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenter);

            template.VisualTree = borderFactory;

            Trigger isMouseOverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            isMouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "border"));
            template.Triggers.Add(isMouseOverTrigger);

            Trigger isPressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            isPressedTrigger.Setters.Add(new Setter(Border.OpacityProperty, 0.82, "border"));
            template.Triggers.Add(isPressedTrigger);

            btn.Template = template;
            return btn;
        }

        private void UpdatePathStatusUI()
        {
            if (_zaloInfo != null && _zaloInfo.IsValid)
            {
                _badgeZaloStatus.Background = new SolidColorBrush(Color.FromArgb(40, 40, 167, 69));
                _txtPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(60, 200, 100));
                _txtPathStatus.Text = string.Format("✓ ĐÃ NHẬN DIỆN (v{0})", _zaloInfo.Version);
                _txtZaloPath.Text = _zaloInfo.ExePath;

                // Show toolbar buttons and profile list
                _actionBtnsPanel.Visibility = Visibility.Visible;
                _emptyStateNotice.Visibility = Visibility.Collapsed;
                _panelProfiles.Visibility = Visibility.Visible;

                EnsureDefaultProfileIfEmpty();
            }
            else
            {
                // First launch state: only show scan and browse buttons!
                _badgeZaloStatus.Background = new SolidColorBrush(Color.FromArgb(40, 220, 53, 69));
                _txtPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(240, 80, 90));
                _txtPathStatus.Text = "✕ CHƯA CHỌN ZALO";
                _txtZaloPath.Text = "Vui lòng bấm 'Tự động quét' hoặc 'Chọn thủ công' để bắt đầu.";

                // Hide toolbar buttons and profile list, show empty state notice
                _actionBtnsPanel.Visibility = Visibility.Collapsed;
                _emptyStateNotice.Visibility = Visibility.Visible;
                _panelProfiles.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderProfileCards()
        {
            if (_zaloInfo == null || !_zaloInfo.IsValid) return;

            _cardMap.Clear();
            _panelProfiles.Children.Clear();

            for (int i = 0; i < _profiles.Count; i++)
            {
                Profile p = _profiles[i];
                _panelProfiles.Children.Add(CreateProfileCard(p, i + 1));
            }
        }

        private UIElement CreateProfileCard(Profile profile, int index)
        {
            bool isRootApp = (profile.Id == "1");
            bool isRunning = LauncherService.IsProfileRunning(profile, _zaloInfo != null ? _zaloInfo.ExePath : null);

            // Distinctive card styling for App Gốc vs Clones
            Border card = new Border
            {
                Background = isRootApp ? new SolidColorBrush(Color.FromRgb(18, 25, 38)) : new SolidColorBrush(Color.FromRgb(26, 29, 38)),
                BorderBrush = isRootApp ? new SolidColorBrush(Color.FromRgb(0, 140, 255)) : new SolidColorBrush(Color.FromRgb(40, 44, 58)),
                BorderThickness = isRootApp ? new Thickness(2) : new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 12)
            };

            Grid cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); // Avatar
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

            // Avatar Preview Box (Click to Launch app!)
            Grid avatarBox = new Grid
            {
                Width = 52,
                Height = 52,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                ToolTip = string.Format("👉 Nhấp vào avatar để mở Zalo ({0})", profile.Name)
            };

            Border avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(0, 104, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = isRootApp ? new SolidColorBrush(Color.FromRgb(0, 180, 255)) : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                BorderThickness = isRootApp ? new Thickness(2) : new Thickness(1.5)
            };

            // Play overlay on hover to make it 100% obvious clicking runs the app
            Border playOverlay = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            TextBlock txtPlay = new TextBlock
            {
                Text = "▶",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            playOverlay.Child = txtPlay;

            // Avatar hover animation
            avatarBox.MouseEnter += delegate
            {
                playOverlay.Visibility = Visibility.Visible;
                avatarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 205, 255));
                avatarBorder.BorderThickness = new Thickness(2.5);
            };
            avatarBox.MouseLeave += delegate
            {
                if (_cardMap.ContainsKey(profile.Id) && _cardMap[profile.Id].IsLaunching)
                {
                    playOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    playOverlay.Visibility = Visibility.Collapsed;
                }
                avatarBorder.BorderBrush = isRootApp ? new SolidColorBrush(Color.FromRgb(0, 180, 255)) : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                avatarBorder.BorderThickness = isRootApp ? new Thickness(2) : new Thickness(1.5);
            };

            if (!string.IsNullOrEmpty(profile.AvatarPath) && File.Exists(profile.AvatarPath))
            {
                try
                {
                    byte[] imgBytes = File.ReadAllBytes(profile.AvatarPath);
                    using (MemoryStream ms = new MemoryStream(imgBytes))
                    {
                        BitmapImage img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.DecodePixelWidth = 96;
                        img.EndInit();
                        img.Freeze();
                        avatarBorder.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                    }
                }
                catch { }
            }
            else
            {
                TextBlock zLetter = new TextBlock
                {
                    Text = "Z",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Child = zLetter;
            }
            avatarBox.Children.Add(avatarBorder);
            avatarBox.Children.Add(playOverlay);

            // Index badge
            Border indexBadge = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = isRootApp ? new SolidColorBrush(Color.FromRgb(0, 130, 255)) : new SolidColorBrush(Color.FromRgb(255, 120, 0)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            TextBlock txtIndex = new TextBlock
            {
                Text = isRootApp ? "★" : profile.Id,
                FontSize = isRootApp ? 12 : 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            indexBadge.Child = txtIndex;
            avatarBox.Children.Add(indexBadge);

            Grid.SetColumn(avatarBox, 0);
            cardGrid.Children.Add(avatarBox);

            // Info Column
            StackPanel infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            StackPanel titleLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Profile Name (Plain text display, not clicked directly)
            TextBlock txtName = new TextBlock
            {
                Text = profile.Name,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            titleLine.Children.Add(txtName);

            // Root App Tag Badge
            if (isRootApp)
            {
                Border tagRoot = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(45, 0, 140, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBlock txtRoot = new TextBlock
                {
                    Text = "★ APP GỐC (CHÍNH)",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 205, 255))
                };
                tagRoot.Child = txtRoot;
                titleLine.Children.Add(tagRoot);
            }

            // Status Pill: ĐANG CHẠY (green) or ĐÃ DỪNG (RED!)
            Border statusPill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isRunning ? new SolidColorBrush(Color.FromArgb(35, 40, 167, 69)) : new SolidColorBrush(Color.FromArgb(45, 220, 53, 69)),
                BorderBrush = isRunning ? new SolidColorBrush(Color.FromArgb(80, 40, 167, 69)) : new SolidColorBrush(Color.FromArgb(140, 220, 53, 69)),
                BorderThickness = new Thickness(1)
            };
            TextBlock txtStatus = new TextBlock
            {
                Text = isRunning ? "● ĐANG CHẠY" : "● ĐÃ DỪNG",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = isRunning ? new SolidColorBrush(Color.FromRgb(46, 204, 113)) : new SolidColorBrush(Color.FromRgb(255, 80, 90))
            };
            statusPill.Child = txtStatus;
            titleLine.Children.Add(statusPill);

            TextBlock txtDir = new TextBlock
            {
                Text = "Dữ liệu: " + profile.DataDir,
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            CheckBox chkAuto = new CheckBox
            {
                Content = "Tự khởi động cùng Windows khi mở máy",
                IsChecked = profile.AutoStart,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 165, 180)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            chkAuto.Checked += delegate
            {
                profile.AutoStart = true;
                string exePath = AutoStartService.GetManagerExecutablePath();
                AutoStartService.SetProfileAutoStart(profile.Id, string.Format("\"{0}\" --launch {1}", exePath, profile.Id), true);
                ProfileService.SaveProfiles(_profiles);
            };
            chkAuto.Unchecked += delegate
            {
                profile.AutoStart = false;
                AutoStartService.SetProfileAutoStart(profile.Id, null, false);
                ProfileService.SaveProfiles(_profiles);
            };

            infoStack.Children.Add(titleLine);
            infoStack.Children.Add(txtDir);
            infoStack.Children.Add(chkAuto);
            Grid.SetColumn(infoStack, 1);
            cardGrid.Children.Add(infoStack);

            // Actions Column
            StackPanel btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Data folder button
            Button btnData = CreateButton("📁 Data", Color.FromRgb(45, 50, 65), Color.FromRgb(60, 65, 80));
            btnData.ToolTip = "Mở thư mục lưu dữ liệu chat & media";
            btnData.Click += delegate
            {
                string targetDir = profile.DataDir;
                if (profile.Id != "1")
                {
                    string zaldat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZalDat" + profile.Id.PadRight(2, '0').Substring(0, 2));
                    if (Directory.Exists(zaldat)) targetDir = zaldat;
                }
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                Process.Start("explorer.exe", targetDir);
            };
            btnData.Margin = new Thickness(4, 0, 4, 0);
            btnStack.Children.Add(btnData);

            // Button Đổi Tên (shows 2 choices: nhập tùy ý hoặc lấy theo user zalo)
            Button btnRename = CreateButton("✏ Đổi Tên", Color.FromRgb(48, 56, 75), Color.FromRgb(65, 78, 105));
            btnRename.ToolTip = "Đổi tên hiển thị (nhập tên tùy ý hoặc lấy theo user Zalo)";
            btnRename.Margin = new Thickness(4, 0, 4, 0);
            btnStack.Children.Add(btnRename);

            // Button Đổi Avatar (select menu dropdown attached directly)
            Button btnAvatar = CreateButton("🖼 Đổi Avatar", Color.FromRgb(50, 70, 100), Color.FromRgb(65, 90, 130));
            btnAvatar.ToolTip = "Chọn ảnh từ máy tính hoặc tự động lấy avatar của user Zalo";
            btnAvatar.Margin = new Thickness(4, 0, 4, 0);
            btnStack.Children.Add(btnAvatar);

            // Register CardControls for dynamic non-churning updates
            CardControls cc = new CardControls();
            cc.Profile = profile;
            cc.StatusPill = statusPill;
            cc.TxtStatus = txtStatus;
            cc.PlayOverlay = playOverlay;
            cc.TxtPlay = txtPlay;
            cc.AvatarBorder = avatarBorder;
            cc.TxtName = txtName;
            _cardMap[profile.Id] = cc;

            // Wire up avatar click to launch with animation
            avatarBox.MouseLeftButtonUp += delegate
            {
                StartLaunchProfileWithAnimation(cc);
            };

            // Wire up Rename dropdown menu
            btnRename.Click += delegate
            {
                ShowRenameMenu(profile, btnRename, cc);
            };

            // Wire up Avatar dropdown menu (no modal window!)
            btnAvatar.Click += delegate
            {
                ShowAvatarMenu(profile, btnAvatar);
            };

            // Stop button ONLY for clone profiles (app gốc không có nút dừng theo yêu cầu)
            if (!isRootApp)
            {
                Button btnStop = CreateButton("⏹ Dừng", Color.FromRgb(180, 40, 50), Color.FromRgb(210, 50, 60));
                btnStop.ToolTip = "Đóng ứng dụng clone này";
                btnStop.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
                btnStop.Click += delegate
                {
                    LauncherService.StopProfile(profile, true);
                    SetCardStatusRunning(cc, false);
                };
                btnStop.Margin = new Thickness(4, 0, 4, 0);
                btnStack.Children.Add(btnStop);
                cc.BtnStop = btnStop;
            }

            // Delete button for clone profiles (profile 2+) with Clean Delete & ModernDialog confirmation
            if (!isRootApp)
            {
                Button btnDelete = CreateButton("🗑", Color.FromRgb(70, 30, 35), Color.FromRgb(120, 40, 45));
                btnDelete.ToolTip = "Xóa hoàn toàn tài khoản clone này và dọn sạch dữ liệu";
                btnDelete.Click += delegate
                {
                    string confirmMsg = string.Format("Bạn có chắc chắn muốn xóa tài khoản '{0}' không?\n\nToàn bộ dữ liệu chat, cache người dùng, phím tắt ngoài Desktop và tiến trình của tài khoản clone này sẽ được dọn sạch hoàn toàn.", profile.Name);
                    if (ModernDialog.Show(this, confirmMsg, "Xác nhận xóa sạch", DialogType.Question, true))
                    {
                        LauncherService.CleanDeleteProfile(profile);
                        _profiles.Remove(profile);
                        ProfileService.SaveProfiles(_profiles);
                        RenderProfileCards();
                        RefreshStorageWidgetAsync();
                        ModernDialog.Show(this, string.Format("Đã xóa sạch tài khoản clone '{0}' và toàn bộ dữ liệu!", profile.Name), "Đã dọn sạch", DialogType.Success);
                    }
                };
                btnDelete.Margin = new Thickness(4, 0, 0, 0);
                btnStack.Children.Add(btnDelete);
            }

            Grid.SetColumn(btnStack, 2);
            cardGrid.Children.Add(btnStack);

            card.Child = cardGrid;
            return card;
        }

        private Border CreateStorageWidget()
        {
            _storageWidgetBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 24, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 52, 70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 6, 14, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Nhấp chuột để quét lại dung lượng Zalo đang chiếm dụng trên máy"
            };

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border iconCircle = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(35, 0, 180, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 180, 255)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            TextBlock txtDiskIcon = new TextBlock
            {
                Text = "💾",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconCircle.Child = txtDiskIcon;
            Grid.SetColumn(iconCircle, 0);
            g.Children.Add(iconCircle);

            StackPanel statsStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };

            StackPanel labelRow = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock lblTitle = new TextBlock
            {
                Text = "DUNG LƯỢNG CHIẾM DỤNG",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 148, 170)),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock btnRefresh = new TextBlock
            {
                Text = " 🔄",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnRefresh.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                RefreshStorageWidgetAsync();
            };
            labelRow.Children.Add(lblTitle);
            labelRow.Children.Add(btnRefresh);

            _txtStorageTotal = new TextBlock
            {
                Text = "Đang tính...",
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                Margin = new Thickness(0, 1, 0, 0)
            };

            _txtStorageBreakdown = new TextBlock
            {
                Text = "Dữ liệu & App Zalo",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 155, 175)),
                Margin = new Thickness(0, 1, 0, 0)
            };

            statsStack.Children.Add(labelRow);
            statsStack.Children.Add(_txtStorageTotal);
            statsStack.Children.Add(_txtStorageBreakdown);
            Grid.SetColumn(statsStack, 1);
            g.Children.Add(statsStack);

            _storageWidgetBorder.Child = g;

            _storageWidgetBorder.MouseEnter += delegate
            {
                _storageWidgetBorder.Background = new SolidColorBrush(Color.FromArgb(230, 28, 33, 48));
                _storageWidgetBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 255));
            };
            _storageWidgetBorder.MouseLeave += delegate
            {
                _storageWidgetBorder.Background = new SolidColorBrush(Color.FromArgb(200, 20, 24, 34));
                _storageWidgetBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 52, 70));
            };
            _storageWidgetBorder.MouseLeftButtonUp += delegate
            {
                RefreshStorageWidgetAsync();
            };

            return _storageWidgetBorder;
        }

        private void RefreshStorageWidgetAsync()
        {
            if (_txtStorageTotal != null)
            {
                _txtStorageTotal.Text = "Đang quét...";
            }

            string exe = (_zaloInfo != null && _zaloInfo.IsValid) ? _zaloInfo.ExePath : null;
            StorageService.CalculateStorageAsync(exe).ContinueWith(t =>
            {
                if (t.IsCompleted && !t.IsFaulted && t.Result != null)
                {
                    StorageInfo info = t.Result;
                    Dispatcher.Invoke(new Action(() =>
                    {
                        if (_txtStorageTotal != null)
                        {
                            _txtStorageTotal.Text = info.TotalFormatted;
                        }
                        if (_txtStorageBreakdown != null)
                        {
                            _txtStorageBreakdown.Text = string.Format("Data: {0} • App: {1}", info.DataFormatted, info.AppFormatted);
                        }
                        if (_storageWidgetBorder != null)
                        {
                            _storageWidgetBorder.ToolTip = string.Format("Dung lượng Zalo đang chiếm dụng trên máy: {0}\n• Dữ liệu (Chat, media, DB, cache): {1}\n• Ứng dụng (App core, instances): {2}\n(Nhấp chuột để quét lại)", info.TotalFormatted, info.DataFormatted, info.AppFormatted);
                        }
                    }));
                }
            });
        }

        private void StartLaunchProfileWithAnimation(CardControls cc)
        {
            if (cc == null || cc.Profile == null || cc.IsLaunching) return;

            if (_zaloInfo == null || !_zaloInfo.IsValid)
            {
                ModernDialog.Show(this, "Vui lòng chọn đường dẫn Zalo.exe trước khi khởi chạy!", "Thông báo", DialogType.Warning);
                return;
            }

            cc.IsLaunching = true;
            cc.StatusPill.Background = new SolidColorBrush(Color.FromArgb(45, 255, 170, 0));
            cc.StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 170, 0));
            cc.TxtStatus.Text = "⌛ ĐANG KHỞI ĐỘNG...";
            cc.TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 40));
            cc.TxtPlay.Text = "⏳";
            cc.PlayOverlay.Visibility = Visibility.Visible;

            System.Threading.Tasks.Task.Run(() =>
            {
                LauncherService.LaunchProfile(cc.Profile, _zaloInfo.ExePath);
                // Background icon sync when window appears, without blocking UI
                System.Threading.Thread.Sleep(2500);
                TaskbarService.SyncAllTaskbarIcons(_profiles, _zaloInfo.ExePath);
                System.Threading.Thread.Sleep(3000);
                TaskbarService.SyncAllTaskbarIcons(_profiles, _zaloInfo.ExePath);
            });
        }

        private void UpdateRunningStatusesOnly()
        {
            if (_zaloInfo == null || !_zaloInfo.IsValid) return;

            // Single pass process detection (5ms) instead of N heavy loops
            var runningIds = LauncherService.GetRunningProfileIds(_profiles);

            foreach (var kvp in _cardMap)
            {
                CardControls cc = kvp.Value;
                bool isRunning = runningIds.Contains(cc.Profile.Id);

                if (cc.IsLaunching)
                {
                    if (isRunning)
                    {
                        cc.IsLaunching = false;
                        SetCardStatusRunning(cc, true);
                    }
                }
                else
                {
                    SetCardStatusRunning(cc, isRunning);
                }
            }
        }

        private void SetCardStatusRunning(CardControls cc, bool isRunning)
        {
            if (cc.IsLaunching) return;

            cc.PlayOverlay.Visibility = Visibility.Collapsed;
            cc.TxtPlay.Text = "▶";

            if (isRunning)
            {
                cc.StatusPill.Background = new SolidColorBrush(Color.FromArgb(35, 40, 167, 69));
                cc.StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 40, 167, 69));
                cc.TxtStatus.Text = "● ĐANG CHẠY";
                cc.TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                if (cc.BtnStop != null) cc.BtnStop.Visibility = Visibility.Visible;
            }
            else
            {
                cc.StatusPill.Background = new SolidColorBrush(Color.FromArgb(45, 220, 53, 69));
                cc.StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 220, 53, 69));
                cc.TxtStatus.Text = "● ĐÃ DỪNG";
                cc.TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 90));
                if (cc.BtnStop != null) cc.BtnStop.Visibility = Visibility.Collapsed;
            }
        }

        private void StartTimer()
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += delegate
            {
                if (_zaloInfo != null && _zaloInfo.IsValid)
                {
                    UpdateRunningStatusesOnly();
                }

                if (Mouse.OverrideCursor != null)
                {
                    Mouse.OverrideCursor = null;
                }
            };
            _statusTimer.Start();

            // Run initial taskbar icon sync in background after startup without blocking UI
            System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Thread.Sleep(2000);
                if (_zaloInfo != null && _zaloInfo.IsValid)
                {
                    TaskbarService.SyncAllTaskbarIcons(_profiles, _zaloInfo.ExePath);
                }
            });
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            _zaloInfo = ZaloDetector.AutoDetect();
            if (_zaloInfo.IsValid)
            {
                _settings.ZaloPath = _zaloInfo.RootPath;
                ProfileService.SaveSettings(_settings);
                EnsureDefaultProfileIfEmpty();
                UpdatePathStatusUI();
                RenderProfileCards();
                RefreshStorageWidgetAsync();
                ModernDialog.Show(this, string.Format("Đã tìm thấy Zalo thành công!\nPhiên bản: {0}\nĐường dẫn: {1}", _zaloInfo.Version, _zaloInfo.ExePath), "Thành công", DialogType.Success);
            }
            else
            {
                ModernDialog.Show(this, "Không tìm thấy Zalo tự động. Vui lòng sử dụng tính năng 'Chọn thủ công' để chỉ định file Zalo.exe.", "Thông báo", DialogType.Warning);
                UpdatePathStatusUI();
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Chọn file Zalo.exe trên máy tính",
                Filter = "Zalo Executable (Zalo.exe)|Zalo.exe;*.exe|All Files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            if (dlg.ShowDialog() == true)
            {
                ZaloInfo info = ZaloDetector.ValidatePath(dlg.FileName);
                if (info.IsValid)
                {
                    _zaloInfo = info;
                    _settings.ZaloPath = _zaloInfo.RootPath;
                    ProfileService.SaveSettings(_settings);
                    EnsureDefaultProfileIfEmpty();
                    UpdatePathStatusUI();
                    RenderProfileCards();
                    RefreshStorageWidgetAsync();
                    ModernDialog.Show(this, string.Format("Đã nhận diện thành công Zalo v{0}!", _zaloInfo.Version), "Thành công", DialogType.Success);
                }
                else
                {
                    ModernDialog.Show(this, "File hoặc thư mục bạn chọn không phải là Zalo PC hợp lệ!", "Lỗi xác thực", DialogType.Error);
                    UpdatePathStatusUI();
                }
            }
        }

        private void BtnAddProfile_Click(object sender, RoutedEventArgs e)
        {
            int nextIndex = _profiles.Count + 1;
            Profile newProf = ProfileService.CreateNewCloneProfile(nextIndex, string.Format("user{0}", nextIndex));

            string iconDir = ProfileService.GetIconsDirectory();
            string ico = IconGenerator.GenerateProfileIcon(_zaloInfo != null ? _zaloInfo.ExePath : null, null, newProf.Id, iconDir);
            newProf.IconPath = ico;

            if (_zaloInfo != null && _zaloInfo.IsValid)
            {
                string baseDir = ProfileService.GetBaseDirectory();
                string instDir = Path.Combine(baseDir, "Instances", "Profile_" + newProf.Id);
                LauncherService.PrepareInstance(instDir, Path.GetDirectoryName(_zaloInfo.ExePath), newProf.Id);
                string instExe = Path.Combine(instDir, "Zalo.exe");
                if (File.Exists(instExe))
                {
                    LauncherService.CreateDesktopShortcut(newProf, instExe);
                }
            }

            _profiles.Add(newProf);
            ProfileService.SaveProfiles(_profiles);
            RenderProfileCards();
            RefreshStorageWidgetAsync();
        }

        private void BtnLaunchAll_Click(object sender, RoutedEventArgs e)
        {
            if (_zaloInfo == null || !_zaloInfo.IsValid) return;

            foreach (var kvp in _cardMap)
            {
                CardControls cc = kvp.Value;
                if (!cc.IsLaunching && !LauncherService.IsProfileRunning(cc.Profile, _zaloInfo.ExePath))
                {
                    StartLaunchProfileWithAnimation(cc);
                }
            }
        }

        private void BtnCloseAll_Click(object sender, RoutedEventArgs e)
        {
            var clones = _profiles.FindAll(p => p.Id != "1");
            LauncherService.StopAllProfiles(clones);
            UpdateRunningStatusesOnly();
        }

        private Border CreateDropdownItem(string icon, string title, string subtitle, Action onClick, Popup parentPop)
        {
            Border item = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock txtIcon = new TextBlock
            {
                Text = icon,
                FontSize = 17,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(txtIcon, 0);
            g.Children.Add(txtIcon);

            StackPanel txtStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock txtTitle = new TextBlock
            {
                Text = title,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            TextBlock txtSub = new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 155, 175)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            txtStack.Children.Add(txtTitle);
            txtStack.Children.Add(txtSub);
            Grid.SetColumn(txtStack, 1);
            g.Children.Add(txtStack);

            item.Child = g;

            item.MouseEnter += delegate
            {
                item.Background = new SolidColorBrush(Color.FromRgb(36, 42, 58));
            };
            item.MouseLeave += delegate
            {
                item.Background = Brushes.Transparent;
            };
            item.MouseLeftButtonUp += delegate
            {
                parentPop.IsOpen = false;
                onClick();
            };
            return item;
        }

        private void ShowAvatarMenu(Profile profile, Button anchorBtn)
        {
            Popup pop = new Popup
            {
                PlacementTarget = anchorBtn,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };

            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 28, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Opacity = 0.75 }
            };

            StackPanel sp = new StackPanel { Width = 320 };

            sp.Children.Add(CreateDropdownItem(
                "📁",
                "Chọn ảnh từ máy tính (Thư mục)...",
                "Chọn file ảnh đại diện từ máy (JPG, PNG, WebP)",
                delegate
                {
                    OpenFileDialog dlg = new OpenFileDialog
                    {
                        Title = "Chọn ảnh đại diện cho tài khoản " + profile.Name,
                        Filter = "Image Files (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|All Files (*.*)|*.*"
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        ApplyAvatarToProfile(profile, dlg.FileName);
                        ModernDialog.Show(this, string.Format("Đã cập nhật avatar thành công cho '{0}'!", profile.Name), "Hoàn tất", DialogType.Success);
                    }
                },
                pop
            ));

            sp.Children.Add(CreateDropdownItem(
                "⚡",
                "Lấy Avatar từ Zalo chính chủ",
                "Tải trực tiếp ảnh đại diện của tài khoản Zalo đã đăng nhập",
                delegate
                {
                    ZaloUserProfile up = ZaloDetector.ExtractAndDownloadUserAvatar(profile.DataDir, profile.Id, ProfileService.GetIconsDirectory());
                    if (up.Success && !string.IsNullOrEmpty(up.DownloadedAvatarPath))
                    {
                        ApplyAvatarToProfile(profile, up.DownloadedAvatarPath);
                        string displayName = !string.IsNullOrEmpty(up.DisplayName) ? up.DisplayName : up.UserId;
                        ModernDialog.Show(this, string.Format("Đã lấy thành công avatar chính chủ của tài khoản Zalo '{0}'!", displayName), "Thành công", DialogType.Success);
                    }
                    else
                    {
                        ModernDialog.Show(this, up.ErrorMessage ?? "Chưa tìm thấy dữ liệu đăng nhập của tài khoản này trên máy.", "Thông báo", DialogType.Warning);
                    }
                },
                pop
            ));

            card.Child = sp;
            pop.Child = card;
            pop.IsOpen = true;
        }

        private void ApplyAvatarToProfile(Profile profile, string imagePath)
        {
            if (profile == null || string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

            profile.AvatarPath = imagePath;
            string iconDir = ProfileService.GetIconsDirectory();
            string ico = IconGenerator.GenerateProfileIcon(_zaloInfo != null ? _zaloInfo.ExePath : null, imagePath, profile.Id, iconDir);
            profile.IconPath = ico;

            TaskbarService.ClearAllIconCaches();

            string targetExe = null;
            if (profile.Id == "1")
            {
                if (_zaloInfo != null && File.Exists(_zaloInfo.ExePath))
                    targetExe = _zaloInfo.ExePath;
            }
            else
            {
                string baseDir = ProfileService.GetBaseDirectory();
                string instDir = Path.Combine(baseDir, "Instances", "Profile_" + profile.Id);
                if (_zaloInfo != null && File.Exists(_zaloInfo.ExePath))
                {
                    LauncherService.PrepareInstance(instDir, Path.GetDirectoryName(_zaloInfo.ExePath), profile.Id);
                }
                string instExe = Path.Combine(instDir, "Zalo.exe");
                if (File.Exists(instExe))
                {
                    targetExe = instExe;
                }
            }

            if (!string.IsNullOrEmpty(targetExe))
            {
                LauncherService.CreateDesktopShortcut(profile, targetExe);
            }

            if (_zaloInfo != null && _zaloInfo.IsValid)
            {
                System.Threading.Tasks.Task.Run(() => TaskbarService.SyncAllTaskbarIcons(_profiles, _zaloInfo.ExePath));
            }

            ProfileService.SaveProfiles(_profiles);
            RenderProfileCards();
        }

        private void ShowRenameMenu(Profile profile, Button anchorBtn, CardControls cc)
        {
            Popup pop = new Popup
            {
                PlacementTarget = anchorBtn,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };

            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 28, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Opacity = 0.75 }
            };

            StackPanel sp = new StackPanel { Width = 320 };

            sp.Children.Add(CreateDropdownItem(
                "✏",
                "Nhập tên tùy ý...",
                "Đặt tên tự do không giới hạn định dạng",
                delegate
                {
                    PromptRenameCustom(profile, cc);
                },
                pop
            ));

            sp.Children.Add(CreateDropdownItem(
                "⚡",
                "Lấy theo tên Zalo chính chủ",
                "Tự động nhận diện tên tài khoản Zalo đã đăng nhập",
                delegate
                {
                    string uName = ZaloDetector.ExtractUserName(profile.DataDir, profile.Id);
                    if (!string.IsNullOrEmpty(uName))
                    {
                        ApplyNewNameToProfile(profile, uName, cc);
                        ModernDialog.Show(this, string.Format("Đã cập nhật tên tài khoản thành '{0}' theo Zalo chính chủ!", uName), "Thành công", DialogType.Success);
                    }
                    else
                    {
                        ModernDialog.Show(this, "Chưa tìm thấy thông tin đăng nhập của tài khoản này trên máy.\nHãy mở Zalo và đăng nhập trước khi lấy tên user.", "Thông báo", DialogType.Warning);
                    }
                },
                pop
            ));

            card.Child = sp;
            pop.Child = card;
            pop.IsOpen = true;
        }

        private void PromptRenameCustom(Profile profile, CardControls cc)
        {
            Window dlg = new Window
            {
                Title = "Đổi tên tài khoản",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            Border rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(12),
                Padding = new Thickness(24, 20, 24, 20),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 6, Opacity = 0.75 }
            };

            StackPanel sp = new StackPanel();

            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lblTitle = new TextBlock
            {
                Text = "✏ ĐỔI TÊN HIỂN THỊ",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblTitle, 0);
            hdr.Children.Add(lblTitle);

            Button btnCloseHdr = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 160)),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnCloseHdr.Click += delegate { dlg.Close(); };
            Grid.SetColumn(btnCloseHdr, 1);
            hdr.Children.Add(btnCloseHdr);
            sp.Children.Add(hdr);

            TextBlock lbl = new TextBlock
            {
                Text = "Nhập tên tài khoản tùy ý (không giới hạn định dạng hay ký tự):",
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 220)),
                FontSize = 13
            };
            sp.Children.Add(lbl);

            TextBox txt = new TextBox
            {
                Text = profile.Name,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(32, 36, 48)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 62, 80)),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
            sp.Children.Add(txt);

            StackPanel btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            Button btnCancel = CreateButton("Hủy bỏ", Color.FromRgb(40, 44, 56), Color.FromRgb(50, 56, 70));
            btnCancel.Width = 90;
            btnCancel.Height = 34;
            btnCancel.Margin = new Thickness(0, 0, 10, 0);
            btnCancel.Click += delegate { dlg.Close(); };

            Button btnOk = CreateButton("Lưu Tên", Color.FromRgb(0, 140, 255), Color.FromRgb(0, 100, 220));
            btnOk.Width = 105;
            btnOk.Height = 34;

            Action doSave = delegate
            {
                string newName = txt.Text.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    ApplyNewNameToProfile(profile, newName, cc);
                }
                dlg.Close();
            };

            btnOk.Click += delegate { doSave(); };
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) doSave();
            };
            dlg.Loaded += delegate
            {
                txt.Focus();
                txt.SelectAll();
            };

            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnOk);
            sp.Children.Add(btnRow);

            rootBorder.Child = sp;
            dlg.Content = rootBorder;

            dlg.MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try { dlg.DragMove(); } catch { }
                }
            };

            dlg.ShowDialog();
        }

        private void ApplyNewNameToProfile(Profile profile, string newName, CardControls cc)
        {
            string oldName = profile.Name;
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string oldLnk = Path.Combine(desktop, oldName + ".lnk");
            if (File.Exists(oldLnk))
            {
                try { File.Delete(oldLnk); } catch { }
            }

            profile.Name = newName;
            ProfileService.SaveProfiles(_profiles);

            if (cc != null && cc.TxtName != null)
            {
                cc.TxtName.Text = newName;
            }

            string targetExe = null;
            if (profile.Id == "1")
            {
                if (_zaloInfo != null && File.Exists(_zaloInfo.ExePath)) targetExe = _zaloInfo.ExePath;
            }
            else
            {
                string instExe = Path.Combine(ProfileService.GetBaseDirectory(), "Instances", "Profile_" + profile.Id, "Zalo.exe");
                if (File.Exists(instExe)) targetExe = instExe;
            }

            if (!string.IsNullOrEmpty(targetExe))
            {
                LauncherService.CreateDesktopShortcut(profile, targetExe);
            }
        }
    }
}
