using System.IO;
using System.Security.Cryptography;

namespace Rightpad.Receiver;

internal static class ReceiverIdentityStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "rightpad", "receiver-id");

    public static byte[] Load(string path, Action<string> diagnostic)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Serialize readers/repairers across processes. Only this tiny startup operation waits.
        using var mutex = new Mutex(false, "Local\\rightpad-identity-" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))));
        bool held;
        try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Receiver identity store is busy.");
        try
        {
            if (File.Exists(path))
            {
                string value = File.ReadAllText(path);
                if (value.Length == 32 && value.All(Uri.IsHexDigit)) return Convert.FromHexString(value);
                diagnostic("receiver_id_corrupt_replaced");
            }
            var id = RandomNumberGenerator.GetBytes(16);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(id)));
                    file.Flush(true);
                }
                File.Move(temp, path, true); // Same-directory atomic publication; no partial identity is visible.
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            diagnostic("receiver_id_created");
            return id;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
