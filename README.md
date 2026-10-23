# ZaloSpace - Trình Quản Lý Đa Tài Khoản Zalo (Multi-Profile Zalo Manager)

**ZaloSpace** là ứng dụng Windows giúp quản lý, khởi chạy và phân tách độc lập nhiều tài khoản Zalo trên cùng một máy tính, hỗ trợ phân biệt tài khoản bằng Avatar trên Taskbar và Desktop.

---

## ✨ Tính Năng Nổi Bật

- **Đa tài khoản độc lập (Multi-Profile)**: Chạy đồng thời nhiều tài khoản Zalo mà không bị ghi đè dữ liệu hay xung đột phiên đăng nhập.
- **Tự động nhận diện tài khoản**: Tự động trích xuất Tên và Avatar chính chủ của từng tài khoản Zalo từ cơ sở dữ liệu cục bộ.
- **Phân biệt trên Taskbar & Desktop**: 
  - Mỗi tài khoản có shortcut Desktop và biểu tượng Taskbar riêng kèm Avatar của tài khoản đó.
  - Gắn `AppUserModelId` riêng biệt giúp Windows Taskbar gom nhóm và hiển thị đúng từng tài khoản.
- **Tối ưu dung lượng ổ đĩa**: Sử dụng kỹ thuật NTFS Hardlink và Junction Link giúp các bản sao dùng chung file thư viện của Zalo gốc, tiết kiệm tới hơn 80% dung lượng.
- **Theo dõi dung lượng trực quan**: Thống kê chi tiết dung lượng chiếm dụng (Dữ liệu chat/media và Bộ cài ứng dụng).
- **Khởi động cùng Windows**: Tùy chọn tự động khởi chạy từng tài khoản khi mở máy.
- **Giao diện hiện đại (Fluent Dark Mode)**: Giao diện tối hiện đại, tùy biến modal xác nhận và popup đổi tên/avatar tiện lợi.

---

## 🛠 Cấu Trúc Dự Án

```
├── ZaloSpace/
│   ├── src/
│   │   ├── Program.cs                   # Điểm khởi chạy ứng dụng
│   │   ├── Models/
│   │   │   └── Profile.cs               # Cấu trúc dữ liệu Profile
│   │   ├── Services/
│   │   │   ├── AutoStartService.cs      # Quản lý khởi động cùng Windows
│   │   │   ├── IconGenerator.cs         # Xử lý cắt tròn, tạo icon đa kích thước (.ico)
│   │   │   ├── LauncherService.cs       # Khởi chạy, vá nhị phân ASAR, tạo hardlink
│   │   │   ├── ProfileService.cs        # Quản lý danh sách cấu hình
│   │   │   ├── StorageService.cs        # Đo đạc và tính toán dung lượng chiếm dụng
│   │   │   ├── TaskbarService.cs        # Đổi Icon Taskbar, AppUserModelId qua Win32 API
│   │   │   └── ZaloDetector.cs          # Quét cài đặt Zalo, đọc SQLite trích xuất Profile
│   │   └── UI/
│   │       ├── MainWindow.cs            # Giao diện chính (WPF)
│   │       └── ModernDialog.cs          # Hộp thoại Modal Dark Theme
│   ├── resources/
│   │   ├── app_icon.ico                 # Biểu tượng ứng dụng
│   │   └── app_icon.jpg                 # Ảnh gốc biểu tượng
│   └── build.ps1                        # Script build tự động bằng csc.exe
├── ZaloSpaceManager.exe                 # File thực thi chạy ngay (không cần cài đặt)
├── ZaloSpaceManager.vbs                 # Script khởi động ẩn console
└── README.md
```

---

## 🚀 Hướng Dẫn Biên Dịch (Build)

Dự án được xây dựng bằng C# WPF thuần, sử dụng compiler `csc.exe` có sẵn trong mọi máy Windows (.NET Framework 4.8), **không yêu cầu cài thêm Visual Studio hay Node.js**:

```powershell
powershell -ExecutionPolicy Bypass -File .\ZaloSpace\build.ps1
```

Sau khi build thành công, file `ZaloSpaceManager.exe` sẽ được tạo ngay ở thư mục gốc.

---

## 💻 Yêu Cầu Hệ Thống

- Hệ điều hành: Windows 10 / Windows 11 (64-bit).
- Đã cài đặt ứng dụng Zalo Desktop chính thức.
