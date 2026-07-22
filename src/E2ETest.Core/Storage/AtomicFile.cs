using System.Text;

namespace E2ETest.Core.Storage;

/// <summary>同目录临时文件写入并原子替换；读取允许 writer 重命名。</summary>
public static class AtomicFile
{
    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static void WriteAllText(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        Exception? primaryError = null;

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, fullPath, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(attempt * 20);
                }
            }
        }
        catch (Exception ex)
        {
            primaryError = ex;
            throw;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch when (primaryError is not null) { }
            }
        }
    }
}
