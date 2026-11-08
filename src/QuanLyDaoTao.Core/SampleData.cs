using System.Globalization;

namespace QuanLyDaoTao.Core;

internal static class SampleData
{
    public static IReadOnlyList<Employee> Employees =>
    [
        Employee(1, "NV001", "BSCKII. Nguyễn Văn An", "Khoa Nội Tổng hợp", "Trưởng khoa", "BSCKII / Tiến sĩ", "2015-03-01"),
        Employee(2, "NV002", "ThS.BS. Trần Thị Bình", "Khoa Cấp cứu", "Bác sĩ điều trị", "BSCKI / Thạc sĩ", "2018-06-15"),
        Employee(3, "NV003", "ĐD. Lê Hoài Nam", "Khoa Ngoại Tổng hợp", "Điều dưỡng viên", "Cử nhân Điều dưỡng", "2020-01-10"),
        Employee(4, "NV004", "BS. Phạm Minh Tuấn", "Khoa Nhi", "Bác sĩ điều trị", "Bác sĩ Đa khoa", "2021-09-01"),
        Employee(5, "NV005", "DS. Hoàng Thị Mai", "Khoa Dược", "Dược sĩ bệnh viện", "Dược sĩ", "2019-11-20"),
        Employee(6, "NV006", "BSCKI. Vũ Hoàng Long", "Khoa Khám Bệnh", "Bác sĩ điều trị", "BSCKI / Thạc sĩ", "2017-04-12"),
        Employee(7, "NV007", "KTV. Đỗ Thanh Hương", "Khoa Xét nghiệm", "Kỹ thuật viên trưởng", "Cao đẳng / Trung cấp", "2022-02-01")
    ];

    public static IReadOnlyList<TrainingActivity> Activities =>
    [
        Activity(1, 1, "Cập nhật Hướng dẫn ĐT Tăng huyết áp 2025", "SHKHKT", "Bệnh viện Đa khoa", "2025-02-15", "2025-02-15", 6, "SH01/2025", "Báo cáo viên"),
        Activity(2, 1, "Hội nghị Tim mạch Toàn quốc 2025", "CME", "Hội Tim mạch học Việt Nam", "2025-05-10", "2025-05-12", 24, "CME-TM-8821"),
        Activity(3, 2, "Cấp cứu Ngừng tuần hoàn nâng cao (ACLS)", "CME", "BV Chợ Rẫy", "2025-03-10", "2025-03-12", 16, "CME-CR-0021"),
        Activity(4, 3, "Kiểm soát Nhiễm khuẩn trong Chăm sóc Vết thương", "SHKHKT", "Bệnh viện Đa khoa", "2025-04-12", "2025-04-12", 4, "SH05/2025"),
        Activity(5, 4, "Cấp cứu Nhi khoa Nâng cao", "CME", "Bệnh viện Nhi Đồng 1", "2025-06-10", "2025-06-14", 26, "ND1-CME-88"),
        Activity(6, 5, "Tập huấn Thực hành Dược lâm sàng", "SHKHKT", "Bệnh viện Đa khoa", "2025-08-10", "2025-08-10", 8, "SH10/2025"),
        Activity(7, 6, "Cập nhật Chẩn đoán Hình ảnh & Lâm sàng", "CME", "Đại học Y Dược TPHCM", "2025-09-15", "2025-09-17", 20, "YDUC-2025"),
        Activity(8, 1, "Sinh hoạt KHKT: Quản lý bệnh mạn tính 2026", "SHKHKT", "Bệnh viện Đa khoa", "2026-01-18", "2026-01-18", 6, "SH02/2026"),
        Activity(9, 1, "Đào tạo Quản lý Chất lượng Bệnh viện", "CME", "Đại học Y Dược TPHCM", "2026-04-01", "2026-04-05", 20, "CME-QLCL-992"),
        Activity(10, 2, "Xử trí Đột quỵ cấp giờ vàng", "SHKHKT", "Bệnh viện Đa khoa", "2026-02-12", "2026-02-12", 8, "SH04/2026"),
        Activity(11, 2, "Hội thảo Cấp cứu chống độc", "CME", "BV Bạch Mai", "2026-05-10", "2026-05-12", 16, "BM-2026-99"),
        Activity(12, 3, "Tập huấn Điều dưỡng lâm sàng Quý 1", "SHKHKT", "Bệnh viện Đa khoa", "2026-02-20", "2026-02-20", 4, "SH06/2026"),
        Activity(13, 4, "Dinh dưỡng Nhi khoa", "CME", "Trường ĐH Y Hà Nội", "2026-03-20", "2026-03-22", 14, "CME-YHN-334"),
        Activity(14, 7, "An toàn Sinh học Phòng Xét nghiệm", "CME", "Viện Pasteur", "2026-02-01", "2026-02-02", 10, "PAS-0012")
    ];

    private static Employee Employee(int id, string code, string name, string department, string title, string degree, string date) => new()
    {
        Id = $"emp-{id}", Code = code, Name = name, Department = department, Title = title, Degree = degree,
        StartDate = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
    };

    private static TrainingActivity Activity(int id, int employee, string title, string type, string provider,
        string start, string end, decimal hours, string certificate, string notes = "") => new()
    {
        Id = $"act-{id}", EmployeeId = $"emp-{employee}", Title = title, Type = type, Provider = provider,
        StartDate = DateTime.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        EndDate = DateTime.ParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Hours = hours, CertificateNumber = certificate, Notes = notes
    };
}
