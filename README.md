# ZaloSpace - Trình Quản Lý Đa Tài Khoản Zalo PC (Multi-Profile Zalo Manager)

<p align="center">
  <img src="resources/app_icon.ico" width="80" height="80" alt="ZaloSpace Logo" />
</p>

**ZaloSpace** là ứng dụng Windows chuyên nghiệp giúp quản lý, nhân bản và khởi chạy độc lập nhiều tài khoản Zalo PC song song trên cùng một máy tính. Hỗ trợ tách biệt hoàn toàn dữ liệu chat, tự động nhận diện danh tính tài khoản, tùy chỉnh icon Taskbar/Desktop và can thiệp vị trí hiển thị thông báo tin nhắn Zalo thời gian thực.

---

## 🌟 Tính Năng Nổi Bật

### 1. Đa tài khoản độc lập (Multi-Profile Isolation)
- Chạy đồng thời không giới hạn tài khoản Zalo trên cùng 1 PC mà không bị ghi đè phiên đăng nhập hay xung đột dữ liệu.
- Phân vùng thư mục dữ liệu (`ZaloData`) riêng biệt, bảo mật tuyệt đối cho từng profile.

### 2. Giao diện Light Theme hiện đại & thân thiện
- Thiết kế tông màu trắng chủ đạo sang trọng, phong cách Fluent / Modern Windows 11.
- Hiển thị widget theo dõi tổng dung lượng chiếm dụng (Dữ liệu chat + Bộ cài đặt Zalo).
- Danh sách tài khoản dạng bảng thẻ với số thứ tự `STT`, Avatar hiển thị trực quan, đường dẫn dữ liệu và nút thao tác nhanh.

### 3. Tùy chỉnh vị trí thông báo Zalo (Notification Position Control)
- **Mô phỏng màn hình máy tính giả lập trực quan**: Gồm 6 vị trí hiển thị popup thông báo tin nhắn:
  - **Hàng trên**: *Góc Trên - Trái* | *Ở Trên - Giữa* | *Góc Trên - Phải*
  - **Hàng dưới**: *Góc Dưới - Trái* | *Ở Dưới - Giữa* | *Góc Dưới - Phải (Mặc định Zalo)*
- **Trực quan hóa trạng thái**: Vị trí được kích hoạt hiển thị màu **ĐỎ**, các vị trí còn lại hiển thị màu **XÁM**.
- **Can thiệp thời gian thực (Win32 Hooks)**: Tự động bắt popup tin nhắn mới của Zalo và định vị lại chính xác đến vị trí người dùng mong muốn.

### 4. Phân biệt tài khoản trên Taskbar & Desktop
- Tự động trích xuất tên hiển thị và ảnh đại diện (Avatar) chính chủ từ SQLite cục bộ của Zalo.
- Tạo phím tắt Desktop riêng biệt kèm Avatar nhận diện cho từng tài khoản.
- Tách nhóm cửa sổ trên thanh Taskbar với `AppUserModelId` riêng biệt.

### 5. Quản lý tác vụ thông minh & Menu ngữ cảnh (`···`)
- Menu tùy chọn tích hợp đầy đủ các chức năng:
  - 🚀 **Mở Zalo / ⏹ Dừng Zalo**
  - 📁 **Mở thư mục Data**
  - 🖥 **Tạo Shortcut ngoài Desktop**
  - 🔔 **Vị trí thông báo Zalo...**
  - 🗑 **Xóa sạch tài khoản clone** (dọn sạch dữ liệu, cache và phím tắt)
- Tùy chọn **Tự khởi động cùng Windows khi mở máy** độc lập cho từng profile.

### 6. Tương thích Smart App Control (Windows 11)
- Đi kèm bộ khởi chạy **Native In-Memory Launcher** (`Mo_ZaloSpaceManager.bat` / `Launch.ps1`) giúp nạp ứng dụng an toàn mà không bao giờ bị chính sách Device Guard hay Smart App Control của Windows 11 chặn.

---

## 📁 Cấu Trúc Dự Án

```
├── ZaloSpace/
│   ├── src/
│   │   ├── Program.cs                         # Điểm khởi chạy ứng dụng (STA Entry Point)
│   │   ├── Models/
│   │   │   ├── Profile.cs                     # Cấu trúc dữ liệu Profile tài khoản
│   │   │   └── ZaloInfo.cs                    # Thông tin cài đặt Zalo PC
│   │   ├── Services/
│   │   │   ├── AutoStartService.cs            # Quản lý khởi động cùng Windows (Registry Run)
│   │   │   ├── IconGenerator.cs               # Xử lý cắt tròn, tạo icon đa kích thước (.ico)
│   │   │   ├── LauncherService.cs             # Khởi chạy Zalo độc lập, tạo shortcut Desktop
│   │   │   ├── NotificationPositionService.cs # Can thiệp vị trí hiển thị popup thông báo Zalo
│   │   │   ├── ProfileService.cs              # Quản lý cấu hình JSON và danh sách Profile
│   │   │   ├── StorageService.cs              # Đo đạc & tính toán dung lượng chiếm dụng
│   │   │   ├── TaskbarService.cs              # Đổi Icon Taskbar, AppUserModelId qua Win32 API
│   │   │   └── ZaloDetector.cs                # Quét cài đặt Zalo, đọc SQLite trích xuất Profile
│   │   └── UI/
│   │       ├── MainWindow.cs                  # Giao diện chính (WPF Light Theme)
│   │       └── ModernDialog.cs                # Hộp thoại Modal hiện đại
│   ├── resources/
│   │   ├── app_icon.ico                       # Biểu tượng ứng dụng (Cat Soldier)
│   │   └── app_icon.jpg                       # Ảnh gốc biểu tượng
│   ├── scripts/
│   │   └── create_shortcut.ps1                # Script tạo shortcut Desktop
│   ├── build.ps1                              # Script build tự động bằng csc.exe
│   ├── Launch.ps1                             # Bộ nạp In-Memory Assembly
│   └── Mo_ZaloSpaceManager.bat                # Bộ khởi chạy không bị SAC chặn
├── Mo_ZaloSpaceManager.bat                    # File khởi chạy nhanh tại thư mục gốc
├── ZaloSpaceManager.exe                       # File thực thi nhị phân chính
└── README.md                                  # Tài liệu hướng dẫn sử dụng
```

---

## 🛠 Hướng Dẫn Biên Dịch (Build)

Dự án được xây dựng hoàn toàn bằng **C# WPF thuần** (.NET Framework 4.8), sử dụng trình biên dịch `csc.exe` có sẵn trong mọi máy tính Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\ZaloSpace\build.ps1
```

File thực thi `ZaloSpaceManager.exe` sẽ được tạo ngay tại thư mục gốc.

---

## 🚀 Hướng Dẫn Khởi Chạy

Bạn có thể mở ứng dụng theo một trong các cách sau:
1. **Phím tắt ngoài Desktop**: Bấm đúp vào biểu tượng `Zalo Space Manager` (icon chú mèo).
2. **File chạy nhanh**: Bấm đúp vào file `Mo_ZaloSpaceManager.bat` trong thư mục gốc.
3. **File thực thi trực tiếp**: Bấm đúp vào `ZaloSpaceManager.exe`.

---

## 💻 Yêu Cầu Hệ Thống

- Hệ điều hành: Windows 10 hoặc Windows 11 (64-bit).
- Đã cài đặt ứng dụng Zalo Desktop chính thức.
