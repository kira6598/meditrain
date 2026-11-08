# MediTrain — Quản lý đào tạo y tế & CME

Ứng dụng desktop C# WPF cho Windows, phát triển từ `draft.html`. Dữ liệu lưu cục bộ bằng SQLite; ứng dụng hoạt động ngoại tuyến và không cần máy chủ cơ sở dữ liệu.

## Chạy bản đóng gói

**Bản sửa phân loại CCHN 1.6.1:** `artifacts/customer/MediTrain-1.6.1.exe` hoặc `artifacts/MediTrain-1.6.1-portable-win-x64.zip`. Bản này loại các trạng thái chờ cấp/chưa có khỏi nhóm có CCHN trên tổng quan và các bộ lọc. Đóng bản đang chạy rồi mở EXE này với dữ liệu đang dùng.

**Bản một file để gửi khách hàng:** `artifacts/customer/MediTrain.exe`.

Chỉ cần gửi file này cho khách dùng **Windows 10/11 64-bit**. Khách lưu file vào máy và nhấp đúp để chạy; ứng dụng đã kèm .NET Runtime và SQLite, không cần cài thêm .NET, SQLite hoặc Microsoft Excel. Có thể gửi `artifacts/MediTrain-portable-win-x64.zip` nếu kênh chuyển file yêu cầu ZIP; bên trong chỉ có `MediTrain.exe`.

Bản một file tự tạo cơ sở dữ liệu ở vị trí bên dưới khi chạy lần đầu. Dữ liệu được lưu riêng trên máy khách và giữ lại khi thay file EXE bằng phiên bản mới.

Nếu nhận gói **kèm dữ liệu**, giải nén toàn bộ thư mục rồi mở `MediTrain.exe`. Khi có sẵn `Data/training.db` bên cạnh EXE, ứng dụng dùng bộ dữ liệu này. Giữ cả thư mục `Data` khi sao chép bộ dữ liệu sang máy khác. Tham số `--database` vẫn được ưu tiên nếu được chỉ định. Bản EXE đứng riêng tiếp tục dùng cơ sở dữ liệu trong `%LOCALAPPDATA%`.

**Bản thư mục đầy đủ** vẫn có sẵn nếu cần:

1. Giải nén toàn bộ `artifacts/MediTrain-win-x64.zip` vào một thư mục có quyền đọc/ghi phù hợp.
2. Mở `QuanLyDaoTao.Desktop.exe` trong thư mục đã giải nén.
3. Thêm nhân viên, sau đó nhập hồ sơ đào tạo; hoặc dùng chức năng nạp dữ liệu mẫu để xem thử.

Bản publish dành cho **Windows 10/11 x64**, kèm .NET Runtime. Giữ nguyên các tệp/thư mục đi kèm tệp EXE khi sao chép sang máy khác. Không cần cài .NET riêng cho bản này.

Lần chạy đầu tạo cơ sở dữ liệu rỗng tại:

```text
%LOCALAPPDATA%\QuanLyDaoTao\training.db
```

Mỗi tài khoản Windows có tệp dữ liệu riêng. Dữ liệu giữ lại khi đóng/mở ứng dụng và khi thay thư mục chương trình bằng bản mới.

Có thể chọn tệp SQLite khác bằng tham số dòng lệnh:

```powershell
.\QuanLyDaoTao.Desktop.exe --database "D:\DuLieu\DaoTao\training.db"
```

## Chức năng và cách sử dụng

- **Tổng quan:** hai nhóm, mỗi nhóm 6 thẻ (4 thẻ số liệu và 2 biểu đồ): toàn bộ nhân viên và nhân viên có CCHN. Tổng nhân viên, số tiết và tỷ lệ đạt được tính riêng theo từng nhóm.
- **Nhân viên:** tìm theo mã/tên, lọc khoa/phòng và CCHN, thêm/sửa/xóa, xem lịch sử đào tạo của từng người. Mã nhân viên phải duy nhất, không phân biệt chữ hoa/thường. Xóa nhân viên sẽ xóa các hồ sơ đào tạo liên quan sau khi xác nhận.
- **Đào tạo & SHKHKT:** ghi tên khóa học, nhân viên, loại SHKHKT/CME, đơn vị tổ chức, thời gian, số tiết, số chứng chỉ và ghi chú. Có tìm kiếm và bộ lọc. Số tiết phải lớn hơn 0; ngày hoàn thành không được trước ngày bắt đầu.
- **Báo cáo:** chọn năm hoặc cặp năm liên tiếp, lọc khoa/phòng, CCHN và kết quả đạt/chưa đạt, xem số tiết còn thiếu. Dữ liệu in/xuất theo bộ lọc đang chọn.
- **Phân tích:** so sánh tỷ lệ đạt theo khoa/phòng, lọc CCHN và xem biểu đồ. Bộ lọc **Tất cả / Có CCHN / Không có CCHN** ở nhân viên, báo cáo và phân tích mặc định là **Có CCHN**. Nhân viên có CCHN khi trường **Số CCHN / GPHN** chứa số giấy phép. Các giá trị **Chờ cấp**, **Chưa có**, **Không có**, ký hiệu thay thế hoặc để trống thuộc nhóm **Không có CCHN**, kể cả ghi chú chờ cấp có ngày hẹn. Chỉ có ngày cấp chưa được tính là có CCHN.
- **Excel:** xuất tệp `.xlsx` mở được bằng Microsoft Excel hoặc phần mềm tương thích.
- **In / PDF:** sau bảng là **một biểu đồ tròn tổng hợp Đạt / Không đạt**, chỉ hiển thị phần trăm. Tỷ lệ Đạt = số người đạt / (số người đạt + số người không đạt) × 100%; tỷ lệ Không đạt tính tương tự. Không in biểu đồ chi tiết theo từng khoa/phòng. Màn thống kê hai tiêu chuẩn giữ hai biểu đồ tổng hợp độc lập cho 48 tiết và 12 tiết theo đúng các năm đã chọn. Bảng và biểu đồ dùng cùng bộ lọc; biểu đồ chuyển sang trang tiếp theo nếu chưa đủ chỗ. Chọn **Microsoft Print to PDF** trong hộp thoại in của Windows để lưu PDF nếu máy đã bật máy in này.
- **Sao lưu:** chọn vị trí lưu bản sao SQLite nhất quán bằng chức năng sao lưu trong ứng dụng.
- **Dữ liệu mẫu:** 7 nhân viên và 14 hồ sơ năm 2025–2026 từ draft. Khi thay dữ liệu hiện có, ứng dụng yêu cầu xác nhận và tạo bản sao lưu trước khi thay.

## Hồ sơ đào tạo chi tiết nhân viên

Vào **Quản lý nhân viên**, chọn một người rồi bấm **Hồ sơ đào tạo**, hoặc nhấp đúp dòng nhân viên.

Màn hình hiển thị mã nhân viên, họ tên, khoa/phòng; hai mục đánh giá riêng **12 tiết/năm** và **48 tiết/2 năm** với bộ chọn năm. Toàn bộ lịch sử nằm trong cùng một bảng **Ngày hoàn thành – Tên khóa học / Buổi SHKHKT – Phân loại – Số tiết – Ngày cấp – Nơi cấp**, gồm hồ sơ đã ghi nhận và từng chứng chỉ/hội thảo/SHKHKT từ Excel. Chọn một dòng để xem khoảng thời gian học và thông tin đối chiếu. Có thể tìm kiếm, lọc loại và khoảng năm; tổng cuối bảng thay đổi theo các dòng đang hiển thị.

Ngày hoàn thành dùng để trình bày lịch sử. Số tiết trong các đánh giá vẫn phân bổ theo **năm bắt đầu khóa học**, thống nhất với các báo cáo của ứng dụng. Bộ lọc danh sách không làm thay đổi kết quả tuân thủ của nhân viên.

Để xóa một chứng chỉ, chọn dòng trong bảng rồi bấm **Xóa chứng chỉ** và xác nhận. Nút này dùng được với chứng chỉ nhập từ Excel, mục chưa xác nhận CME và hồ sơ đã xác nhận. Chứng chỉ cùng thông tin đào tạo liên quan được xóa khỏi nhân viên, không xuất hiện lại dưới dạng dòng nguồn cũ khi mở lại hồ sơ. Nếu chứng chỉ đã được tính số tiết, tổng tích lũy và kết quả tuân thủ được cập nhật ngay.

Các ô Excel có nhiều dòng được tách thành từng dòng trong bảng. Ngày đầy đủ có trong nội dung được nhận diện; ngày hoặc số tiết chưa rõ được hiển thị để bổ sung. Các dòng như `T1: 4` được giữ nguyên, không suy đoán năm hoặc tự quy đổi thành số tiết. Chọn một dòng nhập từ Excel rồi bấm **Bổ sung thông tin** (hoặc nhấp đúp) để hoàn thiện loại CME/SHKHKT, ngày học, đơn vị và số tiết. Sau khi lưu, dòng đó trở thành hồ sơ đào tạo được tính vào báo cáo và không lặp lại trong bảng. Nội dung Excel gốc vẫn được giữ trong hồ sơ nhân viên để đối chiếu.

## Import danh sách nhân viên từ Excel

1. Vào **Quản lý nhân viên → Import Excel → Chọn tệp Excel** và chọn tệp `.xlsx`.
2. Nếu file thiếu cột khoa/phòng, chọn khoa/phòng mặc định. Có thể để **Chưa phân khoa** rồi chỉnh từng hồ sơ sau. Khi đổi lựa chọn này, bấm **Xem trước lại**.
3. Kiểm tra các dòng trong bảng xem trước. Chọn một dòng và mở phần nội dung đầy đủ để đọc các ghi chú dài. Dòng sai định dạng ngày hoặc thiếu dữ liệu bắt buộc phải được sửa trong Excel trước khi nhập.
4. Mặc định **Bỏ qua nhân viên đã tồn tại**. Nếu cần bổ sung hồ sơ cũ, chọn **Cập nhật thông tin có giá trị từ file**; ứng dụng giữ mã nhân viên, liên kết nhật ký đào tạo và thông tin không có trong nguồn. Chế độ cập nhật tạo bản sao SQLite trước khi ghi.
5. Bấm **Nhập dữ liệu** để lưu. Kết quả cho biết số thêm mới, cập nhật và bỏ qua.

Định dạng hồ sơ hỗ trợ tiêu đề gộp trên nhiều hàng như file mẫu: **Họ và tên, Mã số BHXH, Ngày sinh, Chức danh công việc, Giấy phép hành nghề số, Ngày cấp giấy phép hành nghề, Các chứng chỉ chuyên môn, Hội thảo/Tập huấn/Hội nghị, SHKHKT**. Mã BHXH và giấy phép được lưu dạng chữ để giữ số 0 ở đầu; ngày có thể là ngày Excel hoặc văn bản `dd/MM/yyyy`. File thiếu mã nhân viên sẽ được cấp mã tự động.

Thông tin chứng chỉ, hội thảo và SHKHKT nhiều dòng xuất hiện thành từng dòng trong bảng **Hồ sơ đào tạo → Nhật ký đào tạo**; nội dung nguồn có thể sửa trong form nhân viên. Chọn **Bổ sung thông tin** để ghi nhận ngày học và số tiết cụ thể trước khi tính vào báo cáo.

File có trang chứng chỉ chi tiết được nhận diện qua các cột **Họ và tên, Các chứng chỉ chuyên môn / Tên chứng chỉ, Ngày cấp, Ngày học / Thời gian học, Số tiết, Nơi cấp**. Tab **Chứng chỉ bổ sung** cho phép xem từng dòng và ghi chú trước khi nhập. Ô họ tên gộp nhiều dòng được nối đúng phạm vi. Nhóm chưa gộp chỉ được nối khi mã nhân viên, BHXH hoặc giấy phép duy nhất trong nhóm khớp danh sách nhân viên; dòng không xác định được người sẽ báo lỗi.

Ngày cấp được lưu riêng với ngày học. Khoảng học có đủ ngày được tách thành ngày bắt đầu/kết thúc; khoảng chỉ ghi năm được giữ nguyên để bổ sung. Số tiết hỗ trợ số lẻ như 1,5. Ngày cấp sớm hơn ngày kết thúc trong nguồn được ghi chú để đối chiếu, không tự sửa ngày.

Chứng chỉ bổ sung được lưu với phân loại từ nguồn, mặc định **Chứng chỉ**. Dữ liệu này chưa được cộng CME/SHKHKT cho đến khi người dùng chọn **Bổ sung thông tin** để xác nhận loại hình và ngày học đầy đủ. Import lại cùng chứng chỉ cập nhật thông tin bổ sung thay vì tạo trùng; số tiết của hồ sơ đã xác nhận luôn được giữ nguyên. Chế độ bỏ qua nhân viên cũ vẫn cho phép nhập chứng chỉ bổ sung mới mà không thay đổi thông tin cá nhân hiện có.

Định dạng `.xls` cũ cần được lưu thành `.xlsx` trước khi nhập. File Excel nguồn được đọc nguyên trạng. Bản 1.4 tự nâng cấp cơ sở dữ liệu của các bản trước và giữ nguyên nhân viên, chứng chỉ nguồn cùng các hồ sơ đào tạo đã lưu.

## Quy tắc tính báo cáo

Các ngưỡng được triển khai **theo draft cung cấp**:

| Báo cáo | Cách tính | Ngưỡng đạt |
| --- | --- | --- |
| Một năm | SHKHKT + CME trong năm được chọn | Từ 12 tiết |
| Hai năm | Tổng số tiết trong năm bắt đầu và năm liền sau | Từ 48 tiết |

Hai kết quả được đánh giá độc lập. Ví dụ: 0 tiết năm đầu và 48 tiết năm sau vẫn đạt báo cáo hai năm, nhưng năm đầu chưa đạt báo cáo một năm.

Khóa học được tính toàn bộ số tiết vào **năm của ngày bắt đầu**, kể cả khi ngày hoàn thành thuộc năm sau. Số tiết hỗ trợ phần thập phân. Nhân viên chưa có hồ sơ vẫn xuất hiện trong báo cáo với 0 tiết. Tỷ lệ dùng tổng nhân viên trong phạm vi khoa/phòng được chọn làm mẫu số; dữ liệu rỗng cho tỷ lệ 0%.

Mặc định dùng năm hiện tại cho báo cáo một năm và cặp năm trước–năm hiện tại cho báo cáo hai năm. Người dùng có thể đổi năm; không cố định ở 2025–2026. Các ngưỡng trên là yêu cầu phần mềm từ draft, không phải chức năng tự xác minh quy định pháp luật.

## Sao lưu và khôi phục

Ưu tiên dùng nút **Sao lưu** trong ứng dụng, lưu bản sao ở vị trí khác với tệp đang dùng. Nên sao lưu trước khi nhập nhiều dữ liệu hoặc thay dữ liệu mẫu.

Để khôi phục thủ công:

1. Đóng mọi cửa sổ ứng dụng đang sử dụng tệp dữ liệu đó.
2. Giữ lại một bản sao của tệp dữ liệu hiện tại.
3. Sao chép bản sao lưu SQLite vào thư mục dữ liệu, đặt tên `training.db` (hoặc đúng đường dẫn đã truyền bằng `--database`).
4. Nếu có các tệp phụ `training.db-wal` hoặc `training.db-shm` của cơ sở dữ liệu cũ, chỉ xử lý chúng khi đã đóng hoàn toàn ứng dụng; không để các tệp phụ cũ đi kèm tệp dữ liệu vừa khôi phục.
5. Mở ứng dụng và kiểm tra danh sách nhân viên, hồ sơ, báo cáo.

Tệp SQLite chứa dữ liệu nhân viên, vì vậy hãy lưu cả dữ liệu và bản sao lưu tại nơi chỉ người có trách nhiệm truy cập được.

## Phát triển từ mã nguồn

Cần **Windows** và **.NET SDK 10**; lần restore/publish đầu cần Internet để tải NuGet và gói runtime. Có thể mở `QuanLyDaoTao.sln` bằng Visual Studio có workload phát triển desktop .NET, hoặc dùng PowerShell tại thư mục dự án.

```powershell
dotnet restore QuanLyDaoTao.sln
dotnet build QuanLyDaoTao.sln -c Release
dotnet test QuanLyDaoTao.sln -c Release
.\scripts\run.ps1
```

Nếu PowerShell trên máy chặn tệp `.ps1`, chạy trực tiếp (không cần đổi chính sách hệ thống):

```powershell
dotnet run --project src/QuanLyDaoTao.Desktop
```

Kiểm tra khởi tạo và bố cục WPF với cơ sở dữ liệu thử nghiệm riêng:

```powershell
dotnet run --project tools/QuanLyDaoTao.SmokeTests -- --output artifacts/qa
```

Công cụ này kiểm tra 6 màn hình với dữ liệu rỗng, dữ liệu mẫu và cửa sổ 1120×700; mở 5 cửa sổ nhập liệu/chi tiết, kiểm tra phân trang in và lưu ảnh PNG. Nó không đọc hoặc thay đổi cơ sở dữ liệu người dùng.

Chạy với tệp dữ liệu riêng để thử nghiệm:

```powershell
.\scripts\run.ps1 -Database "D:\DuLieu\thu-nghiem.db" -Configuration Release
```

Tạo bản phân phối tự kèm runtime và ZIP:

```powershell
.\scripts\publish.ps1
```

Tạo bản một file dành cho khách hàng:

```powershell
.\scripts\publish-portable.ps1
```

Nếu không chạy script PowerShell, có thể publish trực tiếp rồi gửi riêng file EXE trong `artifacts/single-file` (được phép đổi tên thành `MediTrain.exe`):

```powershell
dotnet publish src/QuanLyDaoTao.Desktop -c Release -p:PublishProfile=Portable-win-x64
```

Kiểm tra bản portable trong thư mục trống với đường dẫn .NET dùng chung bị loại khỏi môi trường tiến trình kiểm thử:

```powershell
dotnet run --project tools/QuanLyDaoTao.SmokeTests -c Release -- --portable-exe artifacts/customer/MediTrain.exe
```

Bài kiểm tra sao chép duy nhất EXE, xác minh .NET và SQLite được tải từ các thư viện nhúng, kiểm tra cửa sổ ứng dụng và cơ sở dữ liệu thử nghiệm rồi dọn thư mục tạm. Nó không truy cập cơ sở dữ liệu người dùng.

Cơ chế đóng gói dựa trên [Single-file deployment của .NET](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview).

Lệnh publish trực tiếp khi không dùng script:

```powershell
dotnet publish src/QuanLyDaoTao.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Kết quả:

```text
artifacts\publish\QuanLyDaoTao.Desktop.exe
artifacts\MediTrain-win-x64.zip
```

Dùng `-SkipZip` nếu chỉ cần thư mục publish. Script không đóng gói cơ sở dữ liệu người dùng. Nên đóng bản EXE đang chạy trong thư mục publish trước khi publish lại để tránh tệp bị khóa.

## Cấu trúc mã nguồn

```text
src/QuanLyDaoTao.Core/       Mô hình, SQLite repository, tính báo cáo, xuất Excel
src/QuanLyDaoTao.Desktop/    WPF, cửa sổ nhập liệu, tổng quan, báo cáo, in
tests/QuanLyDaoTao.Tests/    Kiểm thử dữ liệu, báo cáo, xuất tệp
scripts/                   Chạy ứng dụng và tạo bản phân phối
draft.html                 Bản phác thảo gốc
```

`TrainingRepository` dùng truy vấn có tham số, ràng buộc khóa ngoại, transaction khi thay dữ liệu mẫu và SQLite Backup API. Số tiết lưu chính xác dạng thập phân, không cộng dồn bằng số thực dấu phẩy động. `ReportService` tập trung quy tắc tính báo cáo để UI và xuất tệp dùng chung kết quả.

Tài liệu nền tảng: [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/), [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/).
