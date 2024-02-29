using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Controller
{
    public class HealthChecker
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        byte[] buffer = new byte[4096];
        int port;

        public HealthChecker(int p)
        {
            port = p;

            Connect();
        }

        async void Connect()
        {

            IPAddress address = new IPAddress(0x0100007f);

            await socket.ConnectAsync(new IPEndPoint(address, port));
            Console.WriteLine($"connected to: {address}:{port}");
        }

        internal void Receive()
        {
            int len;

            socket.Send(Encoding.UTF8.GetBytes("beacon"));

            while (true)
            {
                len = socket.Receive(buffer);
                if (len <= 0)
                    continue;
                Console.WriteLine($"received {len} bytes");
                var msg = Encoding.UTF8.GetString(buffer, 0, len);
                Console.WriteLine($"received msg: {msg}");
            }
        }
    }
}

