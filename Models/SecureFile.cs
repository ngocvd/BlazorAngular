using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Linq;

//By AI, chưa kiểm tra đc

namespace BlazorAngular.SecureFile
{
//  Here is a secure and practical approach in C# (.NET 6/7/8+) that does the following:

//  Read the original file in chunks.
//  For each chunk(part) :
//    Take the first 512 bytes of that chunk and encrypt them(using AES-256).
//    Leave the rest of the chunk unencrypted(if any).
//    Write to the part file: [encrypted 512 bytes] + [remaining plain bytes of chunk].
//Bytes 0-3   → Magic "PEH1" (4 bytes)
//Bytes 4-19  → Salt(16 bytes)
//Bytes 20-35 → IV(16 bytes)
//Bytes 36+   → AES-256-CBC(first 512 bytes of part ) + plain remaining bytes of part
  class PartialHeaderEncryptSplitter
  {
    private const int HeaderSize = 512;              // bytes to encrypt per part
    private const int KeySizeBytes = 32;               // AES-256
    private const int IvSizeBytes = 16;
    private const int SaltSizeBytes = 16;
    private const int Pbkdf2Iterations = 600_000;          // decent 2026 value

    public static void SplitAndEncryptHeaders(
        string inputFilePath,
        string outputBaseName,                  // e.g. @"C:\secure\myfile"
        string password,
        long partSizeBytes = 100 * 1024 * 1024) // 100 MB target part size
    {
      byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
      byte[] key = DeriveKey(password, salt, Pbkdf2Iterations, KeySizeBytes);

      byte[] buffer = new byte[81920];   // good read size (~80 KB)

      using var fsIn = File.OpenRead(inputFilePath);
      int partIndex = 0;
      long totalRead = 0;

      while (totalRead < fsIn.Length)
      {
        string partPath = $"{outputBaseName}.part{partIndex:D3}";
        long bytesThisPart = 0;

        using var fsPart = File.Create(partPath);

        // Write per-part header: salt + IV + encrypted first 512 bytes
        // (we repeat salt & derive key each time → independent parts)

        byte[] iv = RandomNumberGenerator.GetBytes(IvSizeBytes);

        // Write metadata so decryption knows it's our format
        fsPart.Write(new byte[] { 0x50, 0x45, 0x48, 0x31 }, 0, 4); // magic "PEH1"
        fsPart.Write(salt, 0, salt.Length);
        fsPart.Write(iv, 0, iv.Length);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var crypto = aes.CreateEncryptor();

        bool isFirstBlockOfPart = true;

        while (bytesThisPart < partSizeBytes && totalRead < fsIn.Length)
        {
          int toRead = (int)Math.Min(buffer.Length, partSizeBytes - bytesThisPart);
          int read = fsIn.Read(buffer, 0, toRead);

          if (read == 0) break;

          if (isFirstBlockOfPart)
          {
            // Encrypt only first 512 bytes of this part
            int toEncrypt = Math.Min(HeaderSize, read);

            byte[] encryptedHeader = EncryptBytes(buffer, 0, toEncrypt, crypto);

            fsPart.Write(encryptedHeader, 0, encryptedHeader.Length);

            // Write remaining bytes of this read (if any) unencrypted
            if (read > toEncrypt)
            {
              fsPart.Write(buffer, toEncrypt, read - toEncrypt);
            }

            isFirstBlockOfPart = false;
          }
          else
          {
            // All subsequent data in this part → plain
            fsPart.Write(buffer, 0, read);
          }

          bytesThisPart += read;
          totalRead += read;
        }

        Console.WriteLine($"Created {Path.GetFileName(partPath)} ({new FileInfo(partPath).Length / 1024.0 / 1024:F1} MB)");
        partIndex++;
      }
    }

    private static byte[] EncryptBytes(byte[] data, int offset, int length, ICryptoTransform encryptor)
    {
      using var ms = new MemoryStream();
      using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
      cs.Write(data, offset, length);
      cs.FlushFinalBlock();
      return ms.ToArray();
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations, int keyBytes)
    {
      using var pbkdf2 = new Rfc2898DeriveBytes(
          Encoding.UTF8.GetBytes(password),
          salt,
          iterations,
          HashAlgorithmName.SHA512);

      return pbkdf2.GetBytes(keyBytes);
    }

    // ────────────────────────────────────────────────
    //               Example usage
    // ────────────────────────────────────────────────
    static void Main()
    {
      try
      {
        string input = @"C:\Temp\largefile.zip";
        string password = "correct-horse-battery-staple-2026!";
        string outPrefix = @"C:\Temp\protected\archive";

        SplitAndEncryptHeaders(input, outPrefix, password, 150 * 1024 * 1024);
      }
      catch (Exception ex)
      {
        Console.WriteLine("Error: " + ex.Message);
      }
    }
  }

class PartDecryptAndJoin
  {
    private const int HeaderSize = 512;
    private const int KeySizeBytes = 32;     // AES-256
    private const int SaltSizeBytes = 16;
    private const int IvSizeBytes = 16;
    private const int MagicLength = 4;
    private const int Pbkdf2Iterations = 600_000;

    private static readonly byte[] MagicBytes = { 0x50, 0x45, 0x48, 0x31 }; // "PEH1"

    /// <summary>
    /// Decrypts only the first 512 bytes of each .partXXX file and joins them into the original file.
    /// Processes files one-by-one → low memory usage, no huge temp file needed.
    /// </summary>
    public static void DecryptPartsAndJoin(
        string partsDirectory,
        string outputFilePath,
        string password,
        string partFilePattern = "*.part*",   // e.g. "archive.part*"
        long expectedPartSizeBytes = 100 * 1024 * 1024)  // used as hint only
    {
      var partFiles = Directory.GetFiles(partsDirectory, partFilePattern)
          .Where(f => Path.GetFileName(f).Contains(".part"))
          .OrderBy(f =>
          {
            var name = Path.GetFileNameWithoutExtension(f);
            var ext = Path.GetExtension(f).TrimStart('.');
            if (ext.StartsWith("part") && int.TryParse(ext.Substring(4), out int num))
              return num;
            return 999999; // fallback
          })
          .ToList();

      if (partFiles.Count == 0)
        throw new FileNotFoundException($"No part files found in {partsDirectory} matching {partFilePattern}");

      Console.WriteLine($"Found {partFiles.Count} part files. Starting decryption and join...");

      using var fsOut = File.Create(outputFilePath);

      foreach (var partPath in partFiles)
      {
        Console.WriteLine($"Processing {Path.GetFileName(partPath)} ...");

        using var fsPart = File.OpenRead(partPath);
        long partLength = fsPart.Length;

        if (partLength < MagicLength + SaltSizeBytes + IvSizeBytes + HeaderSize)
        {
          throw new InvalidDataException($"Part file too small: {partPath}");
        }

        // Read header
        byte[] header = new byte[MagicLength + SaltSizeBytes + IvSizeBytes];
        int read = fsPart.Read(header, 0, header.Length);

        if (read < header.Length)
          throw new InvalidDataException($"Incomplete header in {partPath}");

        // Verify magic
        for (int i = 0; i < MagicLength; i++)
        {
          if (header[i] != MagicBytes[i])
            throw new InvalidDataException($"Invalid magic bytes in {partPath}");
        }

        byte[] salt = new byte[SaltSizeBytes];
        byte[] iv = new byte[IvSizeBytes];
        Array.Copy(header, MagicLength, salt, 0, SaltSizeBytes);
        Array.Copy(header, MagicLength + SaltSizeBytes, iv, 0, IvSizeBytes);

        // Derive key
        byte[] key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();

        // Read encrypted header (at least 512 bytes + padding block)
        // We read a safe amount — usually one or two AES blocks more
        byte[] encryptedHeader = new byte[1024];
        int encryptedRead = fsPart.Read(encryptedHeader, 0, encryptedHeader.Length);

        if (encryptedRead < HeaderSize)
          throw new InvalidDataException($"Not enough encrypted data for header in {partPath}");

        byte[] decryptedHeader = DecryptBytes(encryptedHeader, 0, encryptedRead, decryptor);

        // Write only the original 512 bytes (PKCS7 removes padding automatically)
        int headerBytesToWrite = Math.Min(HeaderSize, decryptedHeader.Length);
        fsOut.Write(decryptedHeader, 0, headerBytesToWrite);

        // Copy the rest of the part as-is (plaintext)
        byte[] buffer = new byte[81920];
        int readBytes;
        while ((readBytes = fsPart.Read(buffer, 0, buffer.Length)) > 0)
        {
          fsOut.Write(buffer, 0, readBytes);
        }

        Console.WriteLine($"  → Done ({partLength / 1024.0 / 1024:F1} MB)");
      }

      Console.WriteLine($"\nReconstruction complete → {outputFilePath}");
      Console.WriteLine($"Total parts processed: {partFiles.Count}");
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
      using var pbkdf2 = new Rfc2898DeriveBytes(
          Encoding.UTF8.GetBytes(password),
          salt,
          Pbkdf2Iterations,
          HashAlgorithmName.SHA512);

      return pbkdf2.GetBytes(KeySizeBytes);
    }

    private static byte[] DecryptBytes(byte[] data, int offset, int length, ICryptoTransform decryptor)
    {
      using var ms = new MemoryStream();
      using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
      {
        cs.Write(data, offset, length);
        cs.FlushFinalBlock();
      }
      return ms.ToArray();
    }

    // ────────────────────────────────────────────────
    //               Example usage
    // ────────────────────────────────────────────────
    public static void Main()
    {
      try
      {
        string partsFolder = @"C:\Temp\protected";
        string outputFile = @"C:\Temp\recovered_original.zip";
        string password = "correct-horse-battery-staple-2026!";

        DecryptPartsAndJoin(
            partsDirectory: partsFolder,
            outputFilePath: outputFile,
            password: password,
            partFilePattern: "archive.part*",   // adjust if your naming is different
            expectedPartSizeBytes: 150 * 1024 * 1024
        );
      }
      catch (Exception ex)
      {
        Console.WriteLine("Error: " + ex.Message);
        if (ex.InnerException != null)
          Console.WriteLine("Inner: " + ex.InnerException.Message);
      }
    }
  }
}
