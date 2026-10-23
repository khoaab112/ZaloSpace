using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ZaloSpaceManager.UI
{
    public enum DialogType
    {
        Success,
        Info,
        Warning,
        Error,
        Question
    }

    public class ModernDialog : Window
    {
        public bool Result { get; private set; }

        public static bool Show(Window owner, string message, string title, DialogType type = DialogType.Info, bool isConfirm = false)
        {
            ModernDialog dlg = new ModernDialog(owner, message, title, type, isConfirm);
            dlg.ShowDialog();
            return dlg.Result;
        }

        public ModernDialog(Window owner, string message, string title, DialogType type, bool isConfirm)
        {
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Background = Brushes.Transparent;
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            Color accentColor;
            string iconChar;
            Color iconBg;

            switch (type)
            {
                case DialogType.Success:
                    accentColor = Color.FromRgb(46, 204, 113); // Green
                    iconChar = "✔";
                    iconBg = Color.FromArgb(40, 46, 204, 113);
                    break;
                case DialogType.Warning:
                    accentColor = Color.FromRgb(241, 196, 15); // Yellow/Amber
                    iconChar = "⚠";
                    iconBg = Color.FromArgb(40, 241, 196, 15);
                    break;
                case DialogType.Error:
                    accentColor = Color.FromRgb(231, 76, 60); // Red
                    iconChar = "✕";
                    iconBg = Color.FromArgb(40, 231, 76, 60);
                    break;
                case DialogType.Question:
                    accentColor = Color.FromRgb(0, 140, 255); // Blue
                    iconChar = "❓";
                    iconBg = Color.FromArgb(40, 0, 140, 255);
                    break;
                default:
                    accentColor = Color.FromRgb(0, 180, 255); // Cyan
                    iconChar = "ℹ";
                    iconBg = Color.FromArgb(40, 0, 180, 255);
                    break;
            }

            Border rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)),
                BorderBrush = new SolidColorBrush(accentColor),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(12),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.75
                }
            };

            Grid mainGrid = new Grid { Margin = new Thickness(24, 20, 24, 20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer buttons

            // Header Row (Icon + Title + Close Button)
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border iconBadge = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(iconBg),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            TextBlock txtIcon = new TextBlock
            {
                Text = iconChar,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBadge.Child = txtIcon;
            Grid.SetColumn(iconBadge, 0);
            headerGrid.Children.Add(iconBadge);

            TextBlock txtTitle = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txtTitle, 1);
            headerGrid.Children.Add(txtTitle);

            Button btnClose = new Button
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
            btnClose.Click += delegate
            {
                Result = false;
                Close();
            };
            Grid.SetColumn(btnClose, 2);
            headerGrid.Children.Add(btnClose);

            Grid.SetRow(headerGrid, 0);
            mainGrid.Children.Add(headerGrid);

            // Message Body
            TextBlock txtMsg = new TextBlock
            {
                Text = message,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Color.FromRgb(215, 220, 235)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 16, 0, 20)
            };
            Grid.SetRow(txtMsg, 1);
            mainGrid.Children.Add(txtMsg);

            // Footer Buttons
            StackPanel btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (isConfirm)
            {
                Button btnCancel = new Button
                {
                    Content = "Hủy bỏ",
                    Width = 90,
                    Height = 34,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 185, 200)),
                    Background = new SolidColorBrush(Color.FromRgb(36, 40, 52)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(60, 65, 80)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                ApplyButtonCornerRadius(btnCancel, 8);
                btnCancel.Click += delegate
                {
                    Result = false;
                    Close();
                };
                btnRow.Children.Add(btnCancel);

                Button btnYes = new Button
                {
                    Content = "Đồng ý",
                    Width = 100,
                    Height = 34,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(accentColor),
                    BorderBrush = Brushes.Transparent,
                    Cursor = Cursors.Hand
                };
                ApplyButtonCornerRadius(btnYes, 8);
                btnYes.Click += delegate
                {
                    Result = true;
                    Close();
                };
                btnRow.Children.Add(btnYes);
            }
            else
            {
                Button btnOk = new Button
                {
                    Content = "Đã hiểu",
                    Width = 105,
                    Height = 34,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(accentColor),
                    BorderBrush = Brushes.Transparent,
                    Cursor = Cursors.Hand
                };
                ApplyButtonCornerRadius(btnOk, 8);
                btnOk.Click += delegate
                {
                    Result = true;
                    Close();
                };
                btnRow.Children.Add(btnOk);
            }

            Grid.SetRow(btnRow, 2);
            mainGrid.Children.Add(btnRow);

            rootBorder.Child = mainGrid;
            Content = rootBorder;

            // Allow dragging the dialog
            MouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        private static void ApplyButtonCornerRadius(Button btn, double r)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(r));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            btn.Template = template;
        }
    }
}
