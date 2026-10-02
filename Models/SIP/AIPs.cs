using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class SipToAipProcessor
{
  private readonly string _sipPath;
  private readonly string _aipOutputPath;
  private readonly string _uuid;               // UUID cho AIP
  private readonly List<PremisEvent> _events = new();

  public SipToAipProcessor(string sipPath, string aipOutputPath)
  {
    _sipPath = sipPath;
    _aipOutputPath = aipOutputPath;
    _uuid = Guid.NewGuid().ToString();
  }

  public async Task<bool> ConvertSipToAipAsync()
  {
    try
    {
      // 1. Tạo cấu trúc thư mục AIP
      CreateAipStructure();

      // 2. Copy nội dung từ SIP
      await CopyContentFromSip();

      // 3. Tính checksum tất cả file → Fixity
      await GenerateFixityInformation();

      // 4. Tạo / bổ sung PREMIS events
      AddPremisEvents();

      // 5. Tạo file METS.xml mới (rất quan trọng!)
      await GenerateNewMetsXml();

      // 6. Tạo manifest (bagit style hoặc đơn giản checksum)
      GenerateManifest();

      // 7. Zip lại thành AIP (tùy chọn)
      // await ZipAipAsync();

      Console.WriteLine($"AIP created successfully! UUID: {_uuid}");
      return true;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error converting SIP to AIP: {ex.Message}");
      return false;
    }
  }

  private void CreateAipStructure()
  {
    var dirs = new[]
    {
            Path.Combine(_aipOutputPath, "metadata", "descriptive"),
            Path.Combine(_aipOutputPath, "metadata", "preservation"),
            Path.Combine(_aipOutputPath, "metadata", "submission"),
            Path.Combine(_aipOutputPath, "representations", "rep1", "data"),
            Path.Combine(_aipOutputPath, "representations", "rep1", "metadata")
        };

    foreach (var dir in dirs)
      Directory.CreateDirectory(dir);
  }

  private async Task CopyContentFromSip()
  {
    // Copy toàn bộ nội dung từ SIP sang representation/rep1/data
    // (có thể lọc, rename, giữ nguyên cấu trúc...)
    string targetData = Path.Combine(_aipOutputPath, "representations", "rep1", "data");

    // Ví dụ copy đơn giản
    foreach (var file in Directory.GetFiles(_sipPath, "*", SearchOption.AllDirectories))
    {
      string relPath = Path.GetRelativePath(_sipPath, file);
      string dest = Path.Combine(targetData, relPath);
      Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
      File.Copy(file, dest, true);
    }

    // Copy metadata sang submission
    string submissionMeta = Path.Combine(_aipOutputPath, "metadata", "submission");
    // Copy các file METS, DC, EAD, ... nếu có
  }

  private async Task GenerateFixityInformation()
  {
    var files = Directory.GetFiles(_aipOutputPath, "*", SearchOption.AllDirectories);

    foreach (var file in files)
    {
      string checksum = await ComputeSha256Async(file);
      // Lưu checksum vào PREMIS hoặc file riêng
      Console.WriteLine($"{Path.GetFileName(file)} → SHA256: {checksum}");
      // Thêm vào list để sau ghi vào METS/PREMIS
    }
  }

  private static async Task<string> ComputeSha256Async(string filePath)
  {
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    byte[] hash = await sha256.ComputeHashAsync(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
  }

  private void AddPremisEvents()
  {
    _events.Add(new PremisEvent
    {
      EventType = "ingestion",
      EventDateTime = DateTime.UtcNow,
      EventDetail = "SIP ingested and transformed to AIP",
      LinkingAgent = "C# SipToAipProcessor v1.0"
    });

    _events.Add(new PremisEvent
    {
      EventType = "fixity check",
      EventDateTime = DateTime.UtcNow,
      EventDetail = "Calculated SHA-256 for all content files"
    });
  }

  private async Task GenerateNewMetsXml()
  {
    // Đây là phần quan trọng nhất - tạo METS.xml mới
    // Bạn nên dùng thư viện METS.NET hoặc tự build XDocument

    var mets = new XDocument(
        new XElement("mets:mets",
            new XAttribute(XNamespace.Xmlns + "mets", "http://www.loc.gov/METS/"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            // ... thêm các phần structMap, fileSec, amdSec (PREMIS), dmdSec...
            new XComment("METS generated from SIP → AIP conversion")
        ));

    string metsPath = Path.Combine(_aipOutputPath, "metadata", "mets.xml");
    await File.WriteAllTextAsync(metsPath, mets.ToString());

    // → Thực tế phần này rất dài, thường dùng thư viện
  }

  private void GenerateManifest()
  {
    // Tạo file manifest-md5.txt hoặc sha256 giống bagit
    // ...
  }
}

public record PremisEvent
{
  public string EventType { get; init; } = "";
  public DateTime EventDateTime { get; init; }
  public string EventDetail { get; init; } = "";
  public string LinkingAgent { get; init; } = "";
}


/*
 Cách tốt nhất & nhanh nhất hiện nay (2025–2026):

Dùng Archivematica → gọi API từ C# (rất mạnh, đã làm sẵn hết)
Dùng thư viện METS.NET + PREMIS.NET (nếu có) hoặc tự build
Tham khảo chuẩn E-ARK SIP/AIP specification (rất chi tiết)
Dùng BagIt.NET để đóng gói + tính checksum
Tích hợp tool bên thứ 3: JHOVE, MediaConch, Siegfried → format identification
 */
