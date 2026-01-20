
/*
 Sự khác biệt chính AIP → DIP (E-ARK)

DIP là một CSIP đã sẵn sàng để Access Software xử lý và hiển thị cho người dùng (Consumer).
Thường chỉ lấy 1 representation từ AIP (vì AIP có thể có nhiều rep: gốc + migrated PDF/A + normalized...).
Loại bỏ hoặc giảm bớt preservation metadata không cần cho access (ví dụ: events migration cũ, fixity của rep không chọn).
Thêm hoặc điều chỉnh metadata cho access:
PREMIS: Thêm event "dissemination", access rights, linking đến Access Software (nếu cần emulation/viewer).
EAD/Dublin Core: Giữ descriptive metadata, có thể bổ sung access instructions.
METS root: @csip:OAISPACKAGETYPE="DIP", chỉ trỏ đến 1 representation.

Thường normalize format cho user-friendly (ví dụ: PDF/A → PDF có font embedded + OCR, hoặc render thumbnail/preview).
Cấu trúc DIP giống AIP/CSIP, nhưng luôn có data (không chỉ metadata).

Quy trình cơ bản:

Unzip AIP (hoặc đọc từ folder).
Chọn representation cần disseminate (ví dụ: rep có format dễ access nhất).
Copy cấu trúc cơ bản (metadata/, representations/repX/).
Cập nhật METS root: thay @csip:OAISPACKAGETYPE="AIP" → "DIP", loại bỏ ref đến rep không dùng.
Cập nhật PREMIS: Thêm event dissemination, giữ fixity của rep chọn, loại bớt event preservation cũ nếu không cần.
(Tùy chọn) Tạo representation mới nếu cần convert format.
Tạo manifest-sha256.txt mới.
Zip lại thành DIP.

Code C# mẫu: Tạo DIP từ AIP (giả sử AIP đã unzip thành folder)
Code này giả định:

AIP có cấu trúc E-ARK chuẩn (mets.xml root, representations/rep1/...).
Chọn representation đầu tiên (rep1) làm DIP representation.
Không convert format (bạn có thể thêm Ghostscript/Pdfium/... để normalize).
 */
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
namespace BlazorAngular.Models
{
  class DipGenerator
  {
    static void Main(string[] args)
    {
      string aipFolder = @"C:\Archive\AIP_TaiLieu_001_v1_extracted";  // Thư mục AIP đã unzip
      string outputDipZip = @"C:\Access\DIP_TaiLieu_001.zip";
      string dipPackageId = "DIP_TaiLieu_001";
      string selectedRep = "rep1";  // Tên representation chọn từ AIP (thay đổi nếu cần rep khác)

      CreateDipFromAip(aipFolder, outputDipZip, dipPackageId, selectedRep);
    }

    static void CreateDipFromAip(string aipRoot, string zipPath, string dipId, string selectedRepName)
    {
      string tempRoot = Path.Combine(Path.GetTempPath(), dipId);
      Directory.CreateDirectory(tempRoot);

      try
      {
        // 1. Copy cấu trúc cơ bản từ AIP
        string metadataDir = Path.Combine(tempRoot, "metadata");
        string representationsDir = Path.Combine(tempRoot, "representations");
        Directory.CreateDirectory(metadataDir);
        Directory.CreateDirectory(representationsDir);

        // Copy descriptive metadata (Dublin Core / EAD / MODS)
        string descriptiveSrc = Path.Combine(aipRoot, "metadata", "descriptive.xml"); // hoặc EAD.xml
        if (File.Exists(descriptiveSrc))
          File.Copy(descriptiveSrc, Path.Combine(metadataDir, Path.GetFileName(descriptiveSrc)), true);

        // Copy selected representation
        string aipRepDir = Path.Combine(aipRoot, "representations", selectedRepName);
        if (!Directory.Exists(aipRepDir))
          throw new Exception($"Representation '{selectedRepName}' không tồn tại trong AIP.");

        string dipRepDir = Path.Combine(representationsDir, selectedRepName);
        Directory.CreateDirectory(dipRepDir);
        CopyDirectory(aipRepDir, dipRepDir);  // Copy data/ và mets.xml của rep nếu có

        // 2. Cập nhật hoặc tạo PREMIS cho DIP (thêm event dissemination)
        string premisPath = Path.Combine(metadataDir, "preservation.xml");
        UpdateOrCreatePremisForDip(premisPath, dipRepDir, dipId);

        // 3. Cập nhật METS root cho DIP
        string metsRootPath = Path.Combine(tempRoot, "mets.xml");
        CreateDipMetsXml(metsRootPath, dipId, metadataDir, representationsDir, selectedRepName);

        // 4. Tạo manifest checksum mới
        string manifestPath = Path.Combine(tempRoot, "manifest-sha256.txt");
        GenerateChecksumManifest(tempRoot, manifestPath);

        // 5. Nén thành ZIP (DIP)
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(tempRoot, zipPath, CompressionLevel.Optimal, false);

        Console.WriteLine($"DIP đã tạo thành công: {zipPath}");
      }
      finally
      {
        if (Directory.Exists(tempRoot))
          Directory.Delete(tempRoot, true);
      }
    }

    static void UpdateOrCreatePremisForDip(string premisPath, string repDataDir, string dipId)
    {
      XNamespace premisNs = "http://www.loc.gov/premis/v3";
      XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

      XElement premis;
      if (File.Exists(premisPath))
      {
        premis = XElement.Load(premisPath);
      }
      else
      {
        premis = new XElement(premisNs + "premis",
            new XAttribute(XNamespace.Xmlns + "premis", premisNs),
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "schemaLocation", "http://www.loc.gov/premis/v3 https://www.loc.gov/standards/premis/v3/premis-3-0.xsd")
        );
      }

      // Thêm event dissemination
      var disseminationEvent = new XElement(premisNs + "event",
          new XElement(premisNs + "eventIdentifier",
              new XElement(premisNs + "eventIdentifierType", "local"),
              new XElement(premisNs + "eventIdentifierValue", $"dissemination_{DateTime.UtcNow:yyyyMMddHHmmss}")
          ),
          new XElement(premisNs + "eventType", "dissemination"),
          new XElement(premisNs + "eventDateTime", DateTime.UtcNow.ToString("o")),
          new XElement(premisNs + "eventDetailInformation",
              new XElement(premisNs + "eventDetail", "Prepared DIP for user access")
          ),
          new XElement(premisNs + "eventOutcomeInformation",
              new XElement(premisNs + "eventOutcome", "success")
          ),
          new XElement(premisNs + "linkingAgentIdentifier",
              new XElement(premisNs + "linkingAgentIdentifierType", "software"),
              new XElement(premisNs + "linkingAgentIdentifierValue", "C# DIP Generator")
          )
      );
      premis.Add(disseminationEvent);

      // Giữ hoặc cập nhật fixity cho files trong rep chọn (đơn giản: copy từ AIP hoặc tính lại)
      foreach (var file in Directory.GetFiles(repDataDir, "*.*", SearchOption.AllDirectories))
      {
        string relPath = Path.GetRelativePath(repDataDir, file).Replace("\\", "/");
        string hash = ComputeSha256(file);

        // Tìm hoặc thêm object với fixity
        var existingObj = premis.Elements(premisNs + "object")
            .FirstOrDefault(o => o.Element(premisNs + "objectIdentifier")?.Element(premisNs + "objectIdentifierValue")?.Value == relPath);

        if (existingObj == null)
        {
          var obj = new XElement(premisNs + "object",
              new XAttribute("xml:id", "obj-" + Path.GetFileName(file)),
              new XElement(premisNs + "objectIdentifier",
                  new XElement(premisNs + "objectIdentifierType", "filepath"),
                  new XElement(premisNs + "objectIdentifierValue", relPath)
              ),
              new XElement(premisNs + "objectCharacteristics",
                  new XElement(premisNs + "fixity",
                      new XElement(premisNs + "messageDigestAlgorithm", "SHA-256"),
                      new XElement(premisNs + "messageDigest", hash)
                  )
              )
          );
          premis.Add(obj);
        }
      }

      var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), premis);
      doc.Save(premisPath);
    }

    static void CreateDipMetsXml(string metsPath, string dipId, string metadataDir, string repsDir, string selectedRep)
    {
      XNamespace metsNs = "http://www.loc.gov/METS/";
      XNamespace csip = "https://dilcis.eu/XML/CSIPExtension";
      XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
      XNamespace xlink = "http://www.w3.org/1999/xlink";

      var mets = new XElement(metsNs + "mets",
          new XAttribute("OBJID", dipId),
          new XAttribute("LABEL", $"DIP {dipId}"),
          new XAttribute(csip + "OAISPACKAGETYPE", "DIP"),
          new XAttribute(XNamespace.Xmlns + "csip", csip),
          new XAttribute(xsi + "schemaLocation", "http://www.loc.gov/METS/ http://www.loc.gov/standards/mets/mets.xsd https://dilcis.eu/XML/CSIPExtension https://dilcis.eu/XML/CSIPExtension METS_CSIPExtension.xsd"),

          new XElement(metsNs + "metsHdr",
              new XAttribute("CREATEDATE", DateTime.UtcNow.ToString("o")),
              new XElement(metsNs + "agent",
                  new XAttribute("TYPE", "OTHER"),
                  new XAttribute("ROLE", "CREATOR"),
                  new XAttribute("OTHERTYPE", "SOFTWARE"),
                  new XElement(metsNs + "name", "C# DIP Generator")
              )
          ),

          // dmdSec (descriptive) - copy từ AIP
          new XElement(metsNs + "dmdSec",
              new XAttribute("ID", "dmd-001"),
              new XElement(metsNs + "mdWrap",
                  new XAttribute("MDTYPE", "OTHER"),
                  new XAttribute("OTHERMDTYPE", "DC"),
                  new XElement(metsNs + "xmlData", XElement.Load(Path.Combine(metadataDir, "descriptive.xml")))
              )
          ),

          // amdSec (PREMIS cho DIP)
          new XElement(metsNs + "amdSec",
              new XAttribute("ID", "amd-001"),
              new XElement(metsNs + "mdWrap",
                  new XAttribute("MDTYPE", "PREMIS"),
                  new XElement(metsNs + "xmlData", XElement.Load(Path.Combine(metadataDir, "preservation.xml")))
              )
          ),

          // fileSec: chỉ ref đến files trong rep chọn
          new XElement(metsNs + "fileSec",
              new XElement(metsNs + "fileGrp",
                  new XAttribute("USE", "DISSEMINATION"),
                  // Ví dụ thêm file (cần loop thực tế từ rep/data)
                  new XElement(metsNs + "file",
                      new XAttribute("ID", "file-001"),
                      new XAttribute("CHECKSUM", ComputeSha256(Path.Combine(repsDir, selectedRep, "data", "example.pdf"))),
                      new XAttribute("CHECKSUMTYPE", "SHA-256"),
                      new XElement(metsNs + "FLocat",
                          new XAttribute("LOCTYPE", "URL"),
                          new XAttribute(xlink + "href", $"representations/{selectedRep}/data/example.pdf")
                      )
                  )
              )
          ),

          // structMap: chỉ trỏ đến rep chọn
          new XElement(metsNs + "structMap",
              new XAttribute("TYPE", "PHYSICAL"),
              new XElement(metsNs + "div",
                  new XElement(metsNs + "div",
                      new XAttribute("TYPE", "representation"),
                      new XAttribute("LABEL", $"Representation: {selectedRep} (dissemination)"),
                      new XElement(metsNs + "fptr", new XAttribute("FILEID", "file-001"))
                  )
              )
          )
      );

      var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), mets);
      doc.Save(metsPath);
    }

    static void GenerateChecksumManifest(string rootDir, string manifestPath)
    {
      var sb = new StringBuilder();
      foreach (var file in Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories)
          .Where(f => !f.EndsWith("manifest-sha256.txt")))
      {
        string rel = Path.GetRelativePath(rootDir, file).Replace("\\", "/");
        string hash = ComputeSha256(file);
        sb.AppendLine($"{hash}  {rel}");
      }
      File.WriteAllText(manifestPath, sb.ToString());
    }

    static string ComputeSha256(string filePath)
    {
      using var sha = SHA256.Create();
      using var stream = File.OpenRead(filePath);
      var hash = sha.ComputeHash(stream);
      return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    static void CopyDirectory(string sourceDir, string destinationDir)
    {
      Directory.CreateDirectory(destinationDir);
      foreach (var file in Directory.GetFiles(sourceDir))
      {
        string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
        File.Copy(file, destFile, true);
      }
      foreach (var dir in Directory.GetDirectories(sourceDir))
      {
        string destDir = Path.Combine(destinationDir, Path.GetFileName(dir));
        CopyDirectory(dir, destDir);
      }
    }
  }
}
