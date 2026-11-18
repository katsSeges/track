using System.Text;

namespace Track.Core.Ingest;

/// <summary>
/// Reads complete lines from a stream position in chunks, without loading the file. A line without its
/// closing newline is left for next time. Returns how many bytes were consumed (through the last newline).
/// </summary>
public static class LineReader
{
    public static long ReadCompleteLines(Stream stream, Action<string> onLine, int chunkSize, Action<int> onBytesRead)
    {
        var buffer = new byte[chunkSize];
        using var pending = new MemoryStream();
        long consumed = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            onBytesRead(read);
            var start = 0;
            int newline;
            while ((newline = Array.IndexOf(buffer, (byte)'\n', start, read - start)) >= 0)
            {
                pending.Write(buffer, start, newline - start);
                onLine(Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length).TrimStart('﻿').TrimEnd('\r'));
                consumed += pending.Length + 1;
                pending.SetLength(0);
                start = newline + 1;
            }

            pending.Write(buffer, start, read - start);
        }

        return consumed;
    }
}
