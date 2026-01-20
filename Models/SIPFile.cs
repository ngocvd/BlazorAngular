using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace BlazorAngular.Models
{
  class SIPFile
  {
    static void Main(string[] args)
    {
      string sourceFolder = @"C:\Data\TaiLieuGoc";          // Thư mục chứa tài liệu gốc + metadata
      string outputSipZip = @"C:\Archive\SIP_TaiLieu_001.zip";
      string packageName = "SIP_TaiLieu_001";

      CreateSipPackage(sourceFolder, outputSipZip, packageName);
    }

    static void CreateSipPackage(string sourceDir, string zipPath, string packageId)
    {
      // Tạo thư mục tạm để xây dựng cấu trúc
      string tempRoot = Path.Combine(Path.GetTempPath(), packageId);
      Directory.CreateDirectory(tempRoot);

      try
      {
        // Copy nội dung vào cấu trúc chuẩn
        string metadataDir = Path.Combine(tempRoot, "metadata");
        string dataDir = Path.Combine(tempRoot, "representations", "rep1", "data");
        Directory.CreateDirectory(metadataDir);
        Directory.CreateDirectory(dataDir);

        // Copy tài liệu gốc vào data/
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.TopDirectoryOnly))
        {
          if (Path.GetExtension(file).ToLower() is ".pdf" or ".tif" or ".jpg" or ".xml")
          {
            File.Copy(file, Path.Combine(dataDir, Path.GetFileName(file)), true);
          }
        }

        // Tạo METS đơn giản (bạn nên dùng XmlWriter hoặc thư viện METS)
        string metsPath = Path.Combine(tempRoot, "metadata", "mets.xml");
        File.WriteAllText(metsPath, GenerateSimpleMetsXml(packageId));

        // Tính checksum cho manifest
        string manifestPath = Path.Combine(tempRoot, "manifest-sha256.txt");
        GenerateChecksumManifest(tempRoot, manifestPath);

        // Nén thành ZIP (SIP)
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(tempRoot, zipPath);

        Console.WriteLine($"SIP đã được tạo: {zipPath}");
      }
      finally
      {
        // Xóa thư mục tạm
        Directory.Delete(tempRoot, true);
      }
    }

    static string GenerateSimpleMetsXml(string packageId)
    {
      // Đây chỉ là ví dụ tối thiểu – thực tế nên dùng thư viện METS
      return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<mets:mets xmlns:mets=""http://www.loc.gov/METS/"" OBJID=""{packageId}"">
  <mets:metsHdr CREATEDATE=""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}"">
    <mets:agent TYPE=""OTHER"" ROLE=""CREATOR"" OTHERTYPE=""SOFTWARE"">
      <mets:name>C# SIP Generator</mets:name>
    </mets:agent>
  </mets:metsHdr>
  <!-- Thêm dmdSec, amdSec, fileSec, structMap... -->
</mets:mets>";
    }

    static void GenerateChecksumManifest(string rootDir, string manifestPath)
    {
      var lines = new StringBuilder();
      foreach (var file in Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories))
      {
        if (file.EndsWith("manifest-sha256.txt")) continue;
        string relPath = Path.GetRelativePath(rootDir, file).Replace("\\", "/");
        string hash = ComputeSha256(file);
        lines.AppendLine($"{hash}  {relPath}");
      }
      File.WriteAllText(manifestPath, lines.ToString());
    }

    static string ComputeSha256(string filePath)
    {
      using var sha256 = SHA256.Create();
      using var stream = File.OpenRead(filePath);
      byte[] hash = sha256.ComputeHash(stream);
      return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
  }
}

/*c# đóng gói AIP và SIP lưu trữTrong ngữ cảnh lưu trữ tài liệu điện tử dài hạn (digital preservation, lưu trữ số), AIP và SIP là hai khái niệm quan trọng theo mô hình OAIS (Open Archival Information System):

SIP(Submission Information Package) : Gói thông tin nộp vào kho lưu trữ(thường do người tạo dữ liệu chuẩn bị).
AIP(Archival Information Package) : Gói thông tin lưu trữ chính thức trong kho(được hệ thống archive tạo ra từ SIP, bổ sung thêm metadata bảo quản, fixity, v.v.).

Cả hai gói thường được đóng gói dưới dạng thư mục có cấu trúc chuẩn(thường dùng METS + PREMIS làm metadata chính, kèm các file nội dung tài liệu: PDF/A, TIFF, XML, v.v.), và nén lại thành file zip/tar.
Dưới đây là cách tiếp cận thực tế để tạo / đóng gói SIP và AIP bằng C# (.NET).
1. Cách đơn giản nhất: Đóng gói thủ công(không dùng thư viện chuyên dụng)
Sử dụng System.IO.Compression để tạo file ZIP theo cấu trúc chuẩn.
Cấu trúc ví dụ phổ biến(E-ARK Common Specification hoặc tương tự):
textmy-package-001/
├── metadata/
│   ├── descriptive/       (Dublin Core, EAD, MODS, v.v.dưới dạng XML)
│   ├── preservation/      (PREMIS XML – fixity, events)
│   └── mets.xml(METS root – liên kết tất cả)
├── representations/
│   └── rep1/
│       ├── data/          (file tài liệu: pdf, jpg, tiff,...)
│       └── mets.xml(METS cho representation này – optional)
└── manifest-md5.txt(hoặc SHA-256 checksum của tất cả file) */
