using System;
using System.Security.Cryptography;
using System.Text;

namespace AlterOS.Persistence
{
    public static class GameOperationId
    {
        public static string FromStableKey(string key)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? string.Empty));
                byte[] guid = new byte[16];
                Array.Copy(hash, guid, guid.Length);
                return new Guid(guid).ToString("D");
            }
        }
    }
}
