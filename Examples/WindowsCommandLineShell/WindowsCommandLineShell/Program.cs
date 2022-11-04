using Renci.SshNet;
using System.Text;
/// <summary>
/// Example demonstrating the use of ShellStream On the Windows Console
/// Allowing full colour/key control when connecting to Unix style command shells.
/// 
/// This example makes use of windows command line console which emulates `xterm` emulation 
/// and has Input and Output streams.  Using <see cref="ShellStream" /> 
/// 
/// NOTE: This code is deliberately top down with regions.  So if you intend to reuse 
/// this code please extract the regions to their own methods within a class.
///      
/// </summary>
internal class Program
{
    private static void Main(string[] args)
    {
        #region Authentication 
        /// we are using use password here, but strongly encourage
        /// <see cref=""
        var host = args.Length > 0 ? args[0] : "localhost";
        var user = args.Length > 1 ? args[1] : "sshtestuser";
        /// Passwords - For windows - Try using the windows DPAPI (Data protection API)  
        /// <see href="https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection">)
        /// on Linux dont even bother - just use your .ssh/rsa_key file with <see cref="PrivateKeyAuthenticationMethod" />
        var password = args.Length > 2 ? args[2] : "welovessh!"; 

        var connectionInfo = new PasswordConnectionInfo(host, user, password)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        #endregion

        using (var client = new SshClient(connectionInfo))
        {
            #region Set up the Client Events: Error, ServerKey
            client.ErrorOccurred += (sender, e) =>
            {
                Console.WriteLine($"Error = {e.Exception}");
            };

            client.HostKeyReceived += (sender, e) =>
            {
                // should probably validate it here. We are faking it... see Readme for help.
                Console.WriteLine($"Shell host {host} presented a key named {e.HostKeyName}. Key Accepted."); 

            };
            #endregion

            client.Connect();
            if (client.IsConnected)
            {
                
                #region Get Terminal Shape (using rows and columns, not Pixels)
                const string TermType = "xterm";
                var columns = (uint)Console.WindowWidth;
                var rows = (uint)Console.WindowHeight;
                var noEcho = true;
                var closeConnection = false;
                var bufferSize = 4096;
                #endregion

                #region Acquire Console Streams ignoring the errorstream (use Console.Writeline instead)
                var outStream = Console.OpenStandardOutput();
                var inStream = Console.OpenStandardInput();
                #endregion

                var shellStream = client.CreateShellStream(TermType, columns, rows, 0, 0, bufferSize,
                   (sender, e) => Console.WriteLine("Starting Shell..."));

                #region Configure Shell and Shell Events: DataReceived, ErrorOccurred, Stopping (if available)
                shellStream.DataReceived += (Sender, e) =>
                {
                    outStream.Write(e.Data, 0, e.Data.Length);
                };

                shellStream.ErrorOccurred += (Sender, e) =>
                {
                    closeConnection = true;
                    Console.WriteLine(e.Exception);
                };

                shellStream.Stopping += (sender, e) =>
                {
                    closeConnection = true;
                    if (shellStream != null) Console.WriteLine($"Channel closed by Server {host}");
                    shellStream = null;
                };
                #endregion

                #region Set Up Console and Console Event: CancelKeyPress
                var keyEncoder = new UTF8Encoding();
                ConsoleKeyInfo cki;
                Console.TreatControlCAsInput = true;
                var keyTime = 10;                   // delay between accepting key strokes.

                Console.CancelKeyPress += (sender, e) =>
                {
                    closeConnection = true;
                    if (shellStream != null) shellStream.Flush();
                    Console.Write($"Terminated by User");
                };
                #endregion

                Console.WriteLine($"Connected to {host} using {client.ConnectionInfo.CurrentClientEncryption} CTRL+Break to exit");

                while (!closeConnection && shellStream != null && client.IsConnected)
                {
                    #region Wait for a Key Press
                    /// The <see cref="Console.Readkey()" /> is always blocking.  Unfortunately, you cant 
                    /// use the stream read without loosing the ability to read the key press, so a 
                    /// system timer or a simple thread sleep before Readkey is required to ensure we 
                    /// can act on the external events.
                    Thread.Sleep(keyTime);
                    #endregion
                    cki = Console.ReadKey(noEcho);

                    #region Confirm connection is still good after waiting for the key
                    var cantReadorWrite = !(inStream.CanRead || shellStream.CanWrite);
                    if (cki.Key < 0 || cantReadorWrite) continue;
                    #endregion

                    #region Convert Keypress to UTF8
                    /// Send unicode verison of the key - although more complete solution should 
                    /// define UTF8 Stream readers with <see cref="Console.Out" /> and also set  
                    /// <see "Renci.SshNet.Common.TerminalMode" /> as IUTF8 in the
                    /// <see cref="SshClient.CreateShellStream()" /> method.
                    char[] key = new char[1] { cki.KeyChar };
                    var bytes = keyEncoder.GetBytes(key);
                    #endregion
                    if (shellStream != null)
                    {
                        shellStream.Write(bytes);
                        shellStream.Flush();
                    }
                };
                #region Wrap Up
                inStream.Close();
                outStream.Close();
                client.Disconnect();
                #endregion
            }
        };
    }
}