using System.IO;

namespace CspRpc.HttpClients.TempFileHostClient;

internal interface ITempFileHostClient
{
    /// <summary>
    /// Uploads a file to <c>https://temp.sh</c> and returns the URL to the uploaded file.
    /// </summary>
    /// <param name="data">A <see cref="Stream"/> containing the file data.</param>
    /// <returns>The URL to the uploaded file.</returns>
    Task<string> Upload(Stream data);
}
