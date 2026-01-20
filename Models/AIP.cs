
/*Hiện tại(2026), không có thư viện C# chính thức hoàn chỉnh cho E-ARK AIP (commons-ip-dotnet đã bị archive và thay bằng E-ARK SIP .NET, nhưng chủ yếu hỗ trợ SIP). Vì vậy, cách thực tế nhất là tự xây dựng bằng System.IO, System.Xml.Linq, và tính checksum.
Code mẫu: Tạo AIP từ thư mục SIP đã có(hoặc từ dữ liệu gốc)
Giả sử bạn đã có SIP(hoặc dữ liệu gốc), code này sẽ:

Copy cấu trúc SIP.
Tạo thêm PREMIS events (ví dụ: ingestion, checksum validation).
Cập nhật METS root để thành AIP.
Tạo representation preservation nếu cần(ví dụ: normalize sang PDF/A – phần này bạn có thể mở rộng).
Tính manifest SHA-256.
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
  class AipGenerator
  {
    static void Main(string[] args)
    {
      string sipOrSourceDir = @"C:\Data\SIP_TaiLieu_001";   // Thư mục chứa SIP hoặc dữ liệu gốc
      string outputAipZip = @"C:\Archive\AIP_TaiLieu_001_v1.zip";
      string packageId = "AIP_TaiLieu_001";
      string aipVersion = "v1";  // Tăng version khi migrate format

      CreateAipFromSource(sipOrSourceDir, outputAipZip, packageId, aipVersion);
    }

    static void CreateAipFromSource(string sourceDir, string zipPath, string packageId, string version)
    {
      string tempRoot = Path.Combine(Path.GetTempPath(), packageId + "_" + version);
      Directory.CreateDirectory(tempRoot);

      try
      {
        // 1. Copy cấu trúc từ source (SIP) sang AIP
        string metadataDir = Path.Combine(tempRoot, "metadata");
        string representationsDir = Path.Combine(tempRoot, "representations");
        Directory.CreateDirectory(metadataDir);
        Directory.CreateDirectory(representationsDir);

        // Copy representations/rep1 (gốc) từ source
        string rep1Dir = Path.Combine(representationsDir, "rep1");
        string rep1DataDir = Path.Combine(rep1Dir, "data");
        Directory.CreateDirectory(rep1DataDir);

        // Copy file nội dung (pdf, tiff,...) từ source
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("metadata") && !f.Contains("manifest")))
        {
          string relPath = Path.GetRelativePath(sourceDir, file);
          string dest = Path.Combine(rep1DataDir, relPath);
          Directory.CreateDirectory(Path.GetDirectoryName(dest));
          File.Copy(file, dest, true);
        }

        // 2. Tạo hoặc copy METS descriptive (giả sử source đã có, hoặc tạo mới)
        string metsDescriptive = Path.Combine(metadataDir, "descriptive.xml"); // Dublin Core / MODS
        if (File.Exists(Path.Combine(sourceDir, "metadata", "descriptive.xml")))
          File.Copy(Path.Combine(sourceDir, "metadata", "descriptive.xml"), metsDescriptive, true);
        else
          File.WriteAllText(metsDescriptive, "<dc:title>Tài liệu mẫu</dc:title>"); // placeholder

        // 3. Tạo PREMIS preservation metadata (quan trọng nhất cho AIP)
        string premisPath = Path.Combine(metadataDir, "preservation.xml");
        CreatePremisXml(premisPath, rep1DataDir, packageId);

        // 4. Tạo METS root cho AIP
        string metsRootPath = Path.Combine(tempRoot, "mets.xml");
        CreateAipMetsXml(metsRootPath, packageId, version, metadataDir, rep1Dir);

        // 5. Tạo manifest checksum (SHA-256)
        string manifestPath = Path.Combine(tempRoot, "manifest-sha256.txt");
        GenerateChecksumManifest(tempRoot, manifestPath);

        // 6. Nén thành ZIP (AIP)
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(tempRoot, zipPath, CompressionLevel.Optimal, false);

        Console.WriteLine($"AIP đã tạo thành công: {zipPath}");
      }
      finally
      {
        if (Directory.Exists(tempRoot))
          Directory.Delete(tempRoot, true);
      }
    }

    static void CreatePremisXml(string premisPath, string dataDir, string packageId)
    {
      var premisNs = XNamespace.Get("http://www.loc.gov/premis/v3");
      var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");

      var premis = new XElement(premisNs + "premis",
          new XAttribute(XNamespace.Xmlns + "premis", premisNs),
          new XAttribute(XNamespace.Xmlns + "xsi", xsi),
          new XAttribute(xsi + "schemaLocation", "http://www.loc.gov/premis/v3 https://www.loc.gov/standards/premis/v3/premis-3-0.xsd")
      );

      // Ví dụ: Event ingestion + fixity check
      AddPremisEvent(premis, premisNs, "ingestion", "AIP ingestion by C# tool", "success");
      AddPremisEvent(premis, premisNs, "fixity check", "Checksum validation on ingest", "success");

      // Thêm object cho từng file (fixity)
      foreach (var file in Directory.GetFiles(dataDir, "*.*", SearchOption.AllDirectories))
      {
        string relPath = Path.GetRelativePath(dataDir, file).Replace("\\", "/");
        string hash = ComputeSha256(file);

        var objectEl = new XElement(premisNs + "object",
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
        premis.Add(objectEl);
      }

      var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), premis);
      doc.Save(premisPath);
    }

    static void AddPremisEvent(XElement premis, XNamespace ns, string type, string detail, string outcome)
    {
      var ev = new XElement(ns + "event",
          new XElement(ns + "eventIdentifier",
              new XElement(ns + "eventIdentifierType", "local"),
              new XElement(ns + "eventIdentifierValue", $"{type}_{DateTime.UtcNow:yyyyMMddHHmmss}")
          ),
          new XElement(ns + "eventType", type),
          new XElement(ns + "eventDateTime", DateTime.UtcNow.ToString("o")),
          new XElement(ns + "eventDetailInformation",
              new XElement(ns + "eventDetail", detail)
          ),
          new XElement(ns + "eventOutcomeInformation",
              new XElement(ns + "eventOutcome", outcome)
          ),
          new XElement(ns + "linkingAgentIdentifier",
              new XElement(ns + "linkingAgentIdentifierType", "software"),
              new XElement(ns + "linkingAgentIdentifierValue", "C# AIP Generator")
          )
      );
      premis.Add(ev);
    }

    static void CreateAipMetsXml(string metsPath, string packageId, string version, string metadataDir, string repDir)
    {
      var metsNs = XNamespace.Get("http://www.loc.gov/METS/");
      var csip = XNamespace.Get("https://dilcis.eu/XML/CSIPExtension");
      var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");

      var mets = new XElement(metsNs + "mets",
          new XAttribute("OBJID", packageId),
          new XAttribute("LABEL", $"AIP {packageId} {version}"),
          new XAttribute(csip + "OAISPACKAGETYPE", "AIP"),
          new XAttribute(XNamespace.Xmlns + "csip", csip),
          new XAttribute(xsi + "schemaLocation", "http://www.loc.gov/METS/ http://www.loc.gov/standards/mets/mets.xsd https://dilcis.eu/XML/CSIPExtension https://dilcis.eu/XML/CSIPExtension METS_CSIPExtension.xsd"),

          new XElement(metsNs + "metsHdr",
              new XAttribute("CREATEDATE", DateTime.UtcNow.ToString("o")),
              new XElement(metsNs + "agent",
                  new XAttribute("TYPE", "OTHER"),
                  new XAttribute("ROLE", "CREATOR"),
                  new XAttribute("OTHERTYPE", "SOFTWARE"),
                  new XElement(metsNs + "name", "C# AIP Generator")
              )
          ),

          // dmdSec (descriptive)
          new XElement(metsNs + "dmdSec",
              new XAttribute("ID", "dmd-001"),
              new XElement(metsNs + "mdWrap",
                  new XAttribute("MDTYPE", "OTHER"),
                  new XAttribute("OTHERMDTYPE", "DC"),
                  new XElement(metsNs + "xmlData", XElement.Load(Path.Combine(metadataDir, "descriptive.xml")))
              )
          ),

          // amdSec (preservation + rights + technical)
          new XElement(metsNs + "amdSec",
              new XAttribute("ID", "amd-001"),
              new XElement(metsNs + "mdWrap",
                  new XAttribute("MDTYPE", "PREMIS"),
                  new XElement(metsNs + "xmlData", XElement.Load(Path.Combine(metadataDir, "preservation.xml")))
              )
          ),

          // fileSec
          new XElement(metsNs + "fileSec",
              new XElement(metsNs + "fileGrp",
                  new XAttribute("USE", "ORIGINAL"),
                  // Thêm file từ rep1/data (cần implement đầy đủ nếu nhiều file)
                  new XElement(metsNs + "file",
                      new XAttribute("ID", "file-001"),
                      new XAttribute("CHECKSUM", ComputeSha256(Path.Combine(repDir, "data", "example.pdf"))),
                      new XAttribute("CHECKSUMTYPE", "SHA-256"),
                      new XElement(metsNs + "FLocat",
                          new XAttribute("LOCTYPE", "URL"),
                          new XAttribute("xlink:href", "representations/rep1/data/example.pdf")
                      )
                  )
              )
          ),

          // structMap (physical + logical)
          new XElement(metsNs + "structMap",
              new XAttribute("TYPE", "PHYSICAL"),
              new XElement(metsNs + "div",
                  new XElement(metsNs + "div",
                      new XAttribute("TYPE", "representation"),
                      new XAttribute("LABEL", "Representation 1 (original)"),
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
  }
}
