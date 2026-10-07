using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AromaShooterUdpBridge
{
    public sealed class UdpListener : IDisposable
    {
        private readonly UdpClient client;
        private volatile bool closed;
        public string Address { get; }
        public int Port { get; }
        public UdpListener(string address, int port)
        {
            Address = address; Port = port;
            client = new UdpClient(AddressFamily.InterNetwork);
            try { client.ExclusiveAddressUse = true; client.Client.Bind(new IPEndPoint(IPAddress.Parse(address), port)); }
            catch { client.Dispose(); throw; }
        }
        public void Start(Action<byte[], string> receive, Action<string, bool> log)
        {
            Task.Run(async () => {
                while (!closed)
                {
                    try { var packet = await client.ReceiveAsync().ConfigureAwait(false); if (!closed) receive(packet.Buffer, packet.RemoteEndPoint.ToString()); }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception e) { if (!closed) log("UDP受信エラー " + Address + ":" + Port + " " + e.Message, true); }
                }
            });
        }
        public void Dispose() { closed = true; client.Dispose(); }
    }
}
