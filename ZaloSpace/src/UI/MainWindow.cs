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
            public Border RowBorder;
            public Border LeftIndicator;
            public CheckBox ChkSelect;
            public Border StatusPill;
            public TextBlock TxtStatus;
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
        private Border _badgeIconCircle;
        private TextBlock _txtBadgeIcon;
        private Border _tableWrapper;
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
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
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

            // Start Notification Position Service
            try
            {
                NotificationPositionService.Start(_settings.NotificationPosition ?? "BottomRight");
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                NotificationPositionService.Stop();
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
                Background = Brushes.White,
                Padding = new Thickness(24, 12, 24, 12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            StackPanel headerStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Soldier Cat Logo in circle with cyan border ring (Image 1)
            Border catLogoBorder = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 163, 255)),
                BorderThickness = new Thickness(2),
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

            // Vertical divider line as in Image 1
            Border headerDivider = new Border
            {
                Width = 1,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(headerDivider);

            StackPanel titleGroup = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock titleText = new TextBlock
            {
                Text = "ZALO SPACE MANAGER",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };
            TextBlock subTitleText = new TextBlock
            {
                Text = "Nhân bản & Quản lý song song nhiều tài khoản Zalo PC độc lập | Đầy đủ cuộc gọi & thông báo",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 3, 0, 0)
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

            // 1: Zalo Path Card (Designed cleanly as in Image 2)
            Border pathCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(24, 16, 24, 10),
                Padding = new Thickness(18, 14, 18, 14),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(15, 23, 42),
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Opacity = 0.04
                }
            };

            Grid pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel pathInfo = new StackPanel();
            StackPanel pathHeaderLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            TextBlock lblPath = new TextBlock
            {
                Text = "VỊ TRÍ CÀI ĐẶT ZALO PC:",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            _badgeZaloStatus = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(6, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel badgeContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _badgeIconCircle = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _txtBadgeIcon = new TextBlock
            {
                Text = "✓",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            };
            _badgeIconCircle.Child = _txtBadgeIcon;
            badgeContent.Children.Add(_badgeIconCircle);

            _txtPathStatus = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            badgeContent.Children.Add(_txtPathStatus);
            _badgeZaloStatus.Child = badgeContent;

            pathHeaderLine.Children.Add(lblPath);
            pathHeaderLine.Children.Add(_badgeZaloStatus);

            _txtZaloPath = new TextBlock
            {
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            pathInfo.Children.Add(pathHeaderLine);
            pathInfo.Children.Add(_txtZaloPath);
            Grid.SetColumn(pathInfo, 0);
            pathGrid.Children.Add(pathInfo);

            StackPanel pathBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button btnScan = CreateButton("📁 Tự động quét", Color.FromRgb(0, 120, 255), Color.FromRgb(0, 95, 220));
            btnScan.Click += BtnScan_Click;
            btnScan.Margin = new Thickness(8, 0, 6, 0);

            Button btnBrowse = CreateButton("📁 Chọn thủ công", Color.FromRgb(241, 245, 249), Color.FromRgb(226, 232, 240), new SolidColorBrush(Color.FromRgb(51, 65, 85)), new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1);
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
                Text = "DANH SÁCH TÀI KHOẢN",
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(listTitle, 0);
            toolGrid.Children.Add(listTitle);

            _actionBtnsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _btnAddProfile = CreateButton("+ Thêm Tài Khoản Mới", Color.FromRgb(0, 120, 255), Color.FromRgb(0, 95, 220));
            _btnAddProfile.Click += BtnAddProfile_Click;
            _btnAddProfile.Margin = new Thickness(6, 0, 6, 0);

            _btnLaunchAll = CreateButton("▶ Mở Tất Cả", Color.FromRgb(22, 163, 74), Color.FromRgb(21, 128, 61));
            _btnLaunchAll.Click += BtnLaunchAll_Click;
            _btnLaunchAll.Margin = new Thickness(4, 0, 4, 0);

            _btnCloseAll = CreateButton("⏹ Đóng Tất Cả", Color.FromRgb(220, 38, 38), Color.FromRgb(185, 28, 28));
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
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(36),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(15, 23, 42),
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.04
                }
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
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            TextBlock emptySub = new TextBlock
            {
                Text = "Bấm '📁 Tự động quét' để hệ thống tự tìm Zalo trên máy, hoặc bấm '📁 Chọn thủ công' để chỉ định vị trí Zalo.exe.",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 500
            };
            emptyStack.Children.Add(emptyIcon);
            emptyStack.Children.Add(emptyTitle);
            emptyStack.Children.Add(emptySub);
            _emptyStateNotice.Child = emptyStack;
            contentArea.Children.Add(_emptyStateNotice);

            // Profile List Table Container (matches screenshot)
            _tableWrapper = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(15, 23, 42),
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Opacity = 0.04
                }
            };

            Grid tableGrid = new Grid();
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header row
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Data rows

            // Header row: Tài khoản | Trạng thái | Đường dẫn dữ liệu | Thao tác
            Border tableHeader = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 10, 16, 10)
            };

            Grid thGrid = new Grid();
            thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // Col 0: Checkbox
            thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); // Col 1: STT Index
            thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); // Col 2: Avatar
            thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); // Col 3: Name & Status
            thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 4: Data path & autostart
            thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 5: Actions

            TextBlock thIndex = new TextBlock
            {
                Text = "STT",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(thIndex, 1);
            thGrid.Children.Add(thIndex);

            TextBlock thAccount = new TextBlock
            {
                Text = "Tài khoản",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(thAccount, 2);
            Grid.SetColumnSpan(thAccount, 2);
            thGrid.Children.Add(thAccount);

            TextBlock thDataPath = new TextBlock
            {
                Text = "Đường dẫn dữ liệu",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(thDataPath, 4);
            thGrid.Children.Add(thDataPath);

            TextBlock thActions = new TextBlock
            {
                Text = "Thao tác",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };
            Grid.SetColumn(thActions, 5);
            thGrid.Children.Add(thActions);

            tableHeader.Child = thGrid;
            Grid.SetRow(tableHeader, 0);
            tableGrid.Children.Add(tableHeader);

            // Profile List ScrollViewer
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _panelProfiles = new StackPanel();
            scroll.Content = _panelProfiles;
            Grid.SetRow(scroll, 1);
            tableGrid.Children.Add(scroll);

            _tableWrapper.Child = tableGrid;
            contentArea.Children.Add(_tableWrapper);

            Grid.SetRow(contentArea, 3);
            rootGrid.Children.Add(contentArea);

            // 4: Footer
            Border footer = new Border
            {
                Padding = new Thickness(24, 10, 24, 12),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            TextBlock txtFooter = new TextBlock
            {
                Text = "⚡ Zalo Space Manager - Bản quyền giải pháp phân vùng không gian độc lập cho Zalo PC.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            footer.Child = txtFooter;
            Grid.SetRow(footer, 4);
            rootGrid.Children.Add(footer);

            Content = rootGrid;

            UpdatePathStatusUI();
            RenderProfileCards();
        }

        private Button CreateButton(string text, Color normalColor, Color hoverColor, Brush foreground = null, Brush borderBrush = null, double borderWidth = 0)
        {
            Button btn = new Button
            {
                Content = text,
                Foreground = foreground ?? Brushes.White,
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
            if (borderBrush != null && borderWidth > 0)
            {
                borderFactory.SetValue(Border.BorderBrushProperty, borderBrush);
                borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(borderWidth));
            }
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

        private Button CreateActionButton(string text, string toolTip)
        {
            Button btn = new Button
            {
                Content = text,
                ToolTip = toolTip,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                FontWeight = FontWeights.Medium,
                FontSize = 12,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = null
            };

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(248, 250, 252)));
            borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240)));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenter);

            template.VisualTree = borderFactory;

            Trigger isMouseOverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            isMouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(241, 245, 249)), "border"));
            isMouseOverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(203, 213, 225)), "border"));
            template.Triggers.Add(isMouseOverTrigger);

            Trigger isPressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            isPressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(226, 232, 240)), "border"));
            template.Triggers.Add(isPressedTrigger);

            btn.Template = template;
            return btn;
        }

        private void UpdatePathStatusUI()
        {
            if (_zaloInfo != null && _zaloInfo.IsValid)
            {
                _badgeZaloStatus.Background = new SolidColorBrush(Color.FromRgb(235, 248, 240));
                _badgeZaloStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                _badgeZaloStatus.BorderThickness = new Thickness(1);
                _badgeIconCircle.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                _txtBadgeIcon.Text = "✓";
                _txtPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
                _txtPathStatus.Text = string.Format("ĐÃ NHẬN DIỆN (v{0})", _zaloInfo.Version);
                _txtZaloPath.Text = _zaloInfo.ExePath;
                _txtZaloPath.Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85));

                // Show toolbar buttons and profile list table
                _actionBtnsPanel.Visibility = Visibility.Visible;
                _emptyStateNotice.Visibility = Visibility.Collapsed;
                if (_tableWrapper != null) _tableWrapper.Visibility = Visibility.Visible;
                _panelProfiles.Visibility = Visibility.Visible;

                EnsureDefaultProfileIfEmpty();
            }
            else
            {
                // First launch state: only show scan and browse buttons!
                _badgeZaloStatus.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                _badgeZaloStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                _badgeZaloStatus.BorderThickness = new Thickness(1);
                _badgeIconCircle.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                _txtBadgeIcon.Text = "✕";
                _txtPathStatus.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
                _txtPathStatus.Text = "CHƯA CHỌN ZALO";
                _txtZaloPath.Text = "Vui lòng bấm 'Tự động quét' hoặc 'Chọn thủ công' để bắt đầu.";
                _txtZaloPath.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));

                // Hide toolbar buttons and profile list, show empty state notice
                _actionBtnsPanel.Visibility = Visibility.Collapsed;
                _emptyStateNotice.Visibility = Visibility.Visible;
                if (_tableWrapper != null) _tableWrapper.Visibility = Visibility.Collapsed;
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

            CardControls cc = new CardControls();
            cc.Profile = profile;

            // Row Container Border (matches screenshot table row)
            Border rowBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 10, 16, 10)
            };
            cc.RowBorder = rowBorder;

            Grid rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // Col 0: Indicator & Checkbox
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); // Col 1: STT Index
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); // Col 2: Avatar
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); // Col 3: Name & Status Pill
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 4: Data Path & Auto-start
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 5: Action Buttons

            // --- Col 0: Blue vertical indicator bar & Checkbox ---
            Grid col0Grid = new Grid();
            Border leftIndicator = new Border
            {
                Width = 3,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            cc.LeftIndicator = leftIndicator;
            col0Grid.Children.Add(leftIndicator);

            CheckBox chkSelect = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            chkSelect.Checked += delegate
            {
                leftIndicator.Visibility = Visibility.Visible;
                rowBorder.Background = new SolidColorBrush(Color.FromRgb(240, 247, 255));
            };
            chkSelect.Unchecked += delegate
            {
                leftIndicator.Visibility = Visibility.Collapsed;
                rowBorder.Background = Brushes.White;
            };
            cc.ChkSelect = chkSelect;
            col0Grid.Children.Add(chkSelect);
            Grid.SetColumn(col0Grid, 0);
            rowGrid.Children.Add(col0Grid);

            // --- Col 1: STT Index Number ---
            TextBlock txtIndex = new TextBlock
            {
                Text = index.ToString(),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = isRootApp ? string.Format("Tài khoản {0} (App gốc)", index) : string.Format("Tài khoản {0}", index)
            };
            Grid.SetColumn(txtIndex, 1);
            rowGrid.Children.Add(txtIndex);

            // --- Col 2: Avatar (42x42) ---
            Grid avatarBox = new Grid
            {
                Width = 44,
                Height = 44,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            Border avatarBorder = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                ClipToBounds = true
            };

            Image imgAvatar = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(profile.AvatarPath) && File.Exists(profile.AvatarPath))
            {
                try
                {
                    byte[] imgBytes = File.ReadAllBytes(profile.AvatarPath);
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(imgBytes);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    imgAvatar.Source = bmp;
                }
                catch { imgAvatar.Source = null; }
            }
            if (imgAvatar.Source == null)
            {
                string iconDir = ProfileService.GetIconsDirectory();
                string cachedIcon = Path.Combine(iconDir, "profile_" + profile.Id + ".png");
                if (File.Exists(cachedIcon))
                {
                    try
                    {
                        byte[] imgBytes = File.ReadAllBytes(cachedIcon);
                        BitmapImage bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = new MemoryStream(imgBytes);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgAvatar.Source = bmp;
                    }
                    catch { imgAvatar.Source = null; }
                }
            }
            if (imgAvatar.Source != null)
            {
                avatarBorder.Child = imgAvatar;
            }
            else
            {
                TextBlock txtZ = new TextBlock
                {
                    Text = "Z",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Background = new SolidColorBrush(Color.FromRgb(0, 104, 255));
                avatarBorder.Child = txtZ;
            }
            avatarBox.Children.Add(avatarBorder);

            // Play Overlay on hover
            Border playOverlay = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(140, 15, 23, 42)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed
            };
            TextBlock txtPlay = new TextBlock
            {
                Text = "▶",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            playOverlay.Child = txtPlay;
            avatarBox.Children.Add(playOverlay);

            avatarBox.MouseEnter += delegate { if (!cc.IsLaunching) playOverlay.Visibility = Visibility.Visible; };
            avatarBox.MouseLeave += delegate { if (!cc.IsLaunching) playOverlay.Visibility = Visibility.Collapsed; };
            avatarBox.MouseLeftButtonUp += delegate { StartLaunchProfileWithAnimation(cc); };

            Grid.SetColumn(avatarBox, 2);
            rowGrid.Children.Add(avatarBox);

            // --- Col 3: Name & Status Pill ---
            StackPanel col2Stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 12, 0)
            };

            TextBlock txtName = new TextBlock
            {
                Text = profile.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 165,
                ToolTip = profile.Name
            };
            col2Stack.Children.Add(txtName);

            Border statusPill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            TextBlock txtStatus = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            statusPill.Child = txtStatus;
            col2Stack.Children.Add(statusPill);

            Grid.SetColumn(col2Stack, 3);
            rowGrid.Children.Add(col2Stack);

            // --- Col 4: Data Path & Auto-start Checkbox ---
            StackPanel col3Stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 16, 0)
            };

            TextBlock txtDir = new TextBlock
            {
                Text = profile.DataDir,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 360,
                ToolTip = profile.DataDir
            };
            col3Stack.Children.Add(txtDir);

            CheckBox chkAuto = new CheckBox
            {
                Content = "Tự khởi động cùng Windows khi mở máy",
                IsChecked = profile.AutoStart,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 4, 0, 0),
                Cursor = Cursors.Hand
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
            col3Stack.Children.Add(chkAuto);

            Grid.SetColumn(col3Stack, 4);
            rowGrid.Children.Add(col3Stack);

            // --- Col 5: Action Buttons [✏ Đổi Tên] [🖼 Đổi Avatar] [···] ---
            StackPanel btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // Button Rename
            Button btnRename = CreateActionButton("✏ Đổi Tên", "Đổi tên hiển thị cho tài khoản");
            btnRename.Margin = new Thickness(0, 0, 6, 0);
            btnStack.Children.Add(btnRename);

            // Button Avatar
            Button btnAvatar = CreateActionButton("🖼 Đổi Avatar", "Chọn ảnh đại diện mới hoặc lấy tự động từ Zalo");
            btnAvatar.Margin = new Thickness(0, 0, 6, 0);
            btnStack.Children.Add(btnAvatar);

            // Button More [···]
            Button btnMore = CreateActionButton("···", "Tùy chọn khác (Mở, Dừng, Data, Vị trí thông báo, Shortcut, Xóa)");
            btnMore.MinWidth = 36;
            btnStack.Children.Add(btnMore);

            Grid.SetColumn(btnStack, 5);
            rowGrid.Children.Add(btnStack);

            rowBorder.Child = rowGrid;

            // Row hover effect
            rowBorder.MouseEnter += delegate
            {
                if (chkSelect.IsChecked != true)
                    rowBorder.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            };
            rowBorder.MouseLeave += delegate
            {
                if (chkSelect.IsChecked != true)
                    rowBorder.Background = Brushes.White;
            };

            // Register CardControls
            cc.StatusPill = statusPill;
            cc.TxtStatus = txtStatus;
            cc.PlayOverlay = playOverlay;
            cc.TxtPlay = txtPlay;
            cc.AvatarBorder = avatarBorder;
            cc.TxtName = txtName;
            _cardMap[profile.Id] = cc;

            // Apply initial running state
            SetCardStatusRunning(cc, isRunning);

            // Wire up Rename dropdown
            btnRename.Click += delegate
            {
                ShowRenameMenu(profile, btnRename, cc);
            };

            // Wire up Avatar dropdown
            btnAvatar.Click += delegate
            {
                ShowAvatarMenu(profile, btnAvatar);
            };

            // Wire up More Options dropdown
            btnMore.Click += delegate
            {
                ShowMoreOptionsMenu(profile, btnMore, cc);
            };

            return rowBorder;
        }

        private void ShowMoreOptionsMenu(Profile profile, Button anchorBtn, CardControls cc)
        {
            ContextMenu menu = new ContextMenu
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };

            bool isRunning = LauncherService.IsProfileRunning(profile, _zaloInfo != null ? _zaloInfo.ExePath : null);

            MenuItem itemLaunch = new MenuItem
            {
                Header = "🚀 Mở Zalo",
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 12, 6)
            };
            itemLaunch.Click += delegate
            {
                StartLaunchProfileWithAnimation(cc);
            };
            menu.Items.Add(itemLaunch);

            if (profile.Id != "1" && isRunning)
            {
                MenuItem itemStop = new MenuItem
                {
                    Header = "⏹ Dừng Zalo",
                    FontSize = 12.5,
                    Padding = new Thickness(8, 6, 12, 6),
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
                };
                itemStop.Click += delegate
                {
                    LauncherService.StopProfile(profile, true);
                    SetCardStatusRunning(cc, false);
                };
                menu.Items.Add(itemStop);
            }

            // 📁 Mở thư mục dữ liệu Data (đã chuyển vào menu con theo yêu cầu)
            MenuItem itemData = new MenuItem
            {
                Header = "📁 Mở thư mục Data",
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 12, 6)
            };
            itemData.Click += delegate
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
            menu.Items.Add(itemData);

            MenuItem itemShortcut = new MenuItem
            {
                Header = "🖥 Tạo Shortcut ngoài Desktop",
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 12, 6)
            };
            itemShortcut.Click += delegate
            {
                string targetExe = _zaloInfo != null ? _zaloInfo.ExePath : null;
                if (profile.Id != "1")
                {
                    string instDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZaloInstance_" + profile.Id);
                    string instExe = Path.Combine(instDir, "Zalo.exe");
                    if (File.Exists(instExe)) targetExe = instExe;
                }
                if (!string.IsNullOrEmpty(targetExe))
                {
                    LauncherService.CreateDesktopShortcut(profile, targetExe);
                    ModernDialog.Show(this, string.Format("Đã tạo shortcut cho '{0}' ngoài Desktop thành công!", profile.Name), "Tạo Shortcut", DialogType.Success);
                }
            };
            menu.Items.Add(itemShortcut);

            menu.Items.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });

            // 🔔 Chức năng mới: Vị trí thông báo Zalo
            MenuItem itemNotification = new MenuItem
            {
                Header = "🔔 Vị trí thông báo Zalo...",
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 12, 6)
            };
            itemNotification.Click += delegate
            {
                ShowNotificationPositionModal();
            };
            menu.Items.Add(itemNotification);

            if (profile.Id != "1")
            {
                menu.Items.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });

                MenuItem itemDelete = new MenuItem
                {
                    Header = "🗑 Xóa tài khoản này",
                    FontSize = 12.5,
                    Padding = new Thickness(8, 6, 12, 6),
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
                };
                itemDelete.Click += delegate
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
                menu.Items.Add(itemDelete);
            }

            menu.PlacementTarget = anchorBtn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void ShowNotificationPositionModal()
        {
            Window dlg = new Window
            {
                Title = "Vị trí thông báo Zalo",
                Width = 620,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            Border rootBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(12),
                Padding = new Thickness(24, 20, 24, 20),
                Effect = new DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 24, ShadowDepth = 6, Opacity = 0.15 }
            };

            StackPanel sp = new StackPanel();

            // Header Row (Title & Close Button)
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel();
            TextBlock lblTitle = new TextBlock
            {
                Text = "🖥 VỊ TRÍ THÔNG BÁO TIN NHẮN ZALO",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 255))
            };
            TextBlock lblSub = new TextBlock
            {
                Text = "Chọn 1 trong 6 vị trí trên màn hình máy tính để hiển thị popup thông báo:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 3, 0, 0)
            };
            titleStack.Children.Add(lblTitle);
            titleStack.Children.Add(lblSub);
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            Button btnClose = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            btnClose.Click += delegate { dlg.Close(); };
            Grid.SetColumn(btnClose, 1);
            headerGrid.Children.Add(btnClose);
            sp.Children.Add(headerGrid);

            // Simulated Computer Monitor (Screen + Stand)
            StackPanel monitorContainer = new StackPanel
            {
                Margin = new Thickness(0, 16, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Monitor Outer Bezel / Screen
            Border monitorBezel = new Border
            {
                Width = 530,
                Height = 240,
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Effect = new DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 12, ShadowDepth = 3, Opacity = 0.12 }
            };

            // Screen Interior Grid (Top Row, Wallpaper Area, Bottom Row, Taskbar)
            Grid screenGrid = new Grid();
            screenGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) }); // Row 0: Top Positions
            screenGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 1: Desktop center
            screenGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) }); // Row 2: Bottom Positions
            screenGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) }); // Row 3: Windows Taskbar

            // Desktop center watermark / hint
            TextBlock txtScreenCenter = new TextBlock
            {
                Text = "MÀN HÌNH MÁY TÍNH (DESKTOP)",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(txtScreenCenter, 1);
            screenGrid.Children.Add(txtScreenCenter);

            // Windows Taskbar simulation at bottom of screen
            Border taskbar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            StackPanel taskbarIcons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            TextBlock txtStart = new TextBlock
            {
                Text = "⊞  🔍  📁",
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
            };
            taskbarIcons.Children.Add(txtStart);
            taskbar.Child = taskbarIcons;
            Grid.SetRow(taskbar, 3);
            screenGrid.Children.Add(taskbar);

            // Top Row of 3 Position Boxes
            Grid topRowGrid = new Grid { Margin = new Thickness(10, 8, 10, 0) };
            topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Bottom Row of 3 Position Boxes
            Grid bottomRowGrid = new Grid { Margin = new Thickness(10, 0, 10, 4) };
            bottomRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string currentSelected = _settings.NotificationPosition ?? "BottomRight";
            Dictionary<string, Border> positionBorders = new Dictionary<string, Border>();
            Dictionary<string, TextBlock> positionTexts = new Dictionary<string, TextBlock>();

            Action updateBoxVisuals = delegate
            {
                foreach (var kvp in positionBorders)
                {
                    string posKey = kvp.Key;
                    Border b = kvp.Value;
                    TextBlock tb = positionTexts[posKey];

                    bool isActive = (posKey == currentSelected);
                    if (isActive)
                    {
                        // ACTIVE: Vibrant RED as requested!
                        b.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 28, 28));
                        b.BorderThickness = new Thickness(2);
                        tb.Foreground = Brushes.White;
                        tb.FontWeight = FontWeights.Bold;
                        tb.Text = "✓ " + GetPositionTitle(posKey);
                        b.Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(239, 68, 68),
                            BlurRadius = 8,
                            ShadowDepth = 1,
                            Opacity = 0.4
                        };
                    }
                    else
                    {
                        // INACTIVE: Muted GRAY as requested!
                        b.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                        b.BorderThickness = new Thickness(1.5);
                        tb.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                        tb.FontWeight = FontWeights.Medium;
                        tb.Text = GetPositionTitle(posKey);
                        b.Effect = null;
                    }
                }
            };

            Func<string, HorizontalAlignment, double, Border> createPosBox = delegate(string posKey, HorizontalAlignment halign, double width)
            {
                Border b = new Border
                {
                    Width = width,
                    Height = 50,
                    CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = halign,
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock tb = new TextBlock
                {
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(4)
                };
                b.Child = tb;

                positionBorders[posKey] = b;
                positionTexts[posKey] = tb;

                b.MouseEnter += delegate
                {
                    if (currentSelected != posKey)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                    }
                };
                b.MouseLeave += delegate
                {
                    if (currentSelected != posKey)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                    }
                };
                b.MouseLeftButtonUp += delegate
                {
                    currentSelected = posKey;
                    updateBoxVisuals();
                };

                return b;
            };

            // Top Row Boxes: TopLeft, TopCenter, TopRight
            Border bTopLeft = createPosBox("TopLeft", HorizontalAlignment.Left, 115);
            Grid.SetColumn(bTopLeft, 0);
            topRowGrid.Children.Add(bTopLeft);

            Border bTopCenter = createPosBox("TopCenter", HorizontalAlignment.Center, 155);
            Grid.SetColumn(bTopCenter, 1);
            topRowGrid.Children.Add(bTopCenter);

            Border bTopRight = createPosBox("TopRight", HorizontalAlignment.Right, 115);
            Grid.SetColumn(bTopRight, 2);
            topRowGrid.Children.Add(bTopRight);

            Grid.SetRow(topRowGrid, 0);
            screenGrid.Children.Add(topRowGrid);

            // Bottom Row Boxes: BottomLeft, BottomCenter, BottomRight
            Border bBottomLeft = createPosBox("BottomLeft", HorizontalAlignment.Left, 115);
            Grid.SetColumn(bBottomLeft, 0);
            bottomRowGrid.Children.Add(bBottomLeft);

            Border bBottomCenter = createPosBox("BottomCenter", HorizontalAlignment.Center, 155);
            Grid.SetColumn(bBottomCenter, 1);
            bottomRowGrid.Children.Add(bBottomCenter);

            Border bBottomRight = createPosBox("BottomRight", HorizontalAlignment.Right, 115);
            Grid.SetColumn(bBottomRight, 2);
            bottomRowGrid.Children.Add(bBottomRight);

            Grid.SetRow(bottomRowGrid, 2);
            screenGrid.Children.Add(bottomRowGrid);

            monitorBezel.Child = screenGrid;
            monitorContainer.Children.Add(monitorBezel);

            // Monitor Stand (Neck + Base)
            Border standNeck = new Border
            {
                Width = 42,
                Height = 14,
                Background = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Border standBase = new Border
            {
                Width = 140,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            monitorContainer.Children.Add(standNeck);
            monitorContainer.Children.Add(standBase);

            sp.Children.Add(monitorContainer);

            // Status bar hint
            Border infoBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 16)
            };
            TextBlock txtInfo = new TextBlock
            {
                Text = "💡 Mẹo: Nhấp vào 1 ô trên màn hình để chọn. Ô đang chọn hiển thị màu ĐỎ. Vị trí mặc định gốc của Zalo là Góc Dưới - Phải.",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
            };
            infoBadge.Child = txtInfo;
            sp.Children.Add(infoBadge);

            // Action Buttons Row
            StackPanel btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button btnDefault = CreateButton("Khôi phục mặc định", Color.FromRgb(241, 245, 249), Color.FromRgb(226, 232, 240), new SolidColorBrush(Color.FromRgb(71, 85, 105)), new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1);
            btnDefault.Height = 36;
            btnDefault.Margin = new Thickness(0, 0, 10, 0);
            btnDefault.Click += delegate
            {
                currentSelected = "BottomRight";
                updateBoxVisuals();
            };
            btnRow.Children.Add(btnDefault);

            Button btnCancel = CreateButton("Hủy bỏ", Color.FromRgb(241, 245, 249), Color.FromRgb(226, 232, 240), new SolidColorBrush(Color.FromRgb(71, 85, 105)), new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1);
            btnCancel.Width = 85;
            btnCancel.Height = 36;
            btnCancel.Margin = new Thickness(0, 0, 10, 0);
            btnCancel.Click += delegate { dlg.Close(); };
            btnRow.Children.Add(btnCancel);

            Button btnSave = CreateButton("Lưu & Áp Dụng", Color.FromRgb(0, 120, 255), Color.FromRgb(0, 95, 220));
            btnSave.Width = 125;
            btnSave.Height = 36;
            btnSave.Click += delegate
            {
                _settings.NotificationPosition = currentSelected;
                ProfileService.SaveSettings(_settings);
                NotificationPositionService.SetPosition(currentSelected);
                dlg.Close();
                ModernDialog.Show(this, string.Format("Đã lưu vị trí thông báo: {0}.\n\nKhi có tin nhắn Zalo mới, popup sẽ tự động xuất hiện tại vị trí bạn đã chọn!", GetPositionTitle(currentSelected).Replace("✓ ", "")), "Đã lưu cài đặt", DialogType.Success);
            };
            btnRow.Children.Add(btnSave);

            sp.Children.Add(btnRow);

            rootBorder.Child = sp;
            dlg.Content = rootBorder;

            updateBoxVisuals();
            dlg.ShowDialog();
        }

        private static string GetPositionTitle(string posKey)
        {
            switch (posKey)
            {
                case "TopLeft": return "Góc Trên - Trái";
                case "TopCenter": return "Ở Trên - Giữa";
                case "TopRight": return "Góc Trên - Phải";
                case "BottomLeft": return "Góc Dưới - Trái";
                case "BottomCenter": return "Ở Dưới - Giữa";
                case "BottomRight":
                default:
                    return "Góc Dưới - Phải (Mặc định Zalo)";
            }
        }

        private Border CreateStorageWidget()
        {
            _storageWidgetBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
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
                Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(191, 219, 254)),
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
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock btnRefresh = new TextBlock
            {
                Text = " 🔄",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
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
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
                Margin = new Thickness(0, 1, 0, 0)
            };

            _txtStorageBreakdown = new TextBlock
            {
                Text = "Dữ liệu & App Zalo",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
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
                _storageWidgetBorder.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                _storageWidgetBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            };
            _storageWidgetBorder.MouseLeave += delegate
            {
                _storageWidgetBorder.Background = Brushes.White;
                _storageWidgetBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
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
            cc.StatusPill.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
            cc.StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138));
            cc.TxtStatus.Text = "⌛ ĐANG KHỞI ĐỘNG...";
            cc.TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6));
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
                cc.StatusPill.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
                cc.StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
                cc.TxtStatus.Text = "● Đang chạy";
                cc.TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                cc.StatusPill.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                cc.StatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                cc.TxtStatus.Text = "○ Đã dừng";
                cc.TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
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
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };
            TextBlock txtSub = new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            txtStack.Children.Add(txtTitle);
            txtStack.Children.Add(txtSub);
            Grid.SetColumn(txtStack, 1);
            g.Children.Add(txtStack);

            item.Child = g;

            item.MouseEnter += delegate
            {
                item.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
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
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Effect = new DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 16, ShadowDepth = 4, Opacity = 0.12 }
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
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Effect = new DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 16, ShadowDepth = 4, Opacity = 0.12 }
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
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            Border rootBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(12),
                Padding = new Thickness(24, 20, 24, 20),
                Effect = new DropShadowEffect { Color = Color.FromRgb(15, 23, 42), BlurRadius = 24, ShadowDepth = 6, Opacity = 0.15 }
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
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
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
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
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
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                FontSize = 13
            };
            sp.Children.Add(lbl);

            TextBox txt = new TextBox
            {
                Text = profile.Name,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
            sp.Children.Add(txt);

            StackPanel btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            Button btnCancel = CreateButton("Hủy bỏ", Color.FromRgb(241, 245, 249), Color.FromRgb(226, 232, 240), new SolidColorBrush(Color.FromRgb(71, 85, 105)), new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1);
            btnCancel.Width = 90;
            btnCancel.Height = 34;
            btnCancel.Margin = new Thickness(0, 0, 10, 0);
            btnCancel.Click += delegate { dlg.Close(); };

            Button btnOk = CreateButton("Lưu Tên", Color.FromRgb(0, 120, 255), Color.FromRgb(0, 95, 220));
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
