using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace NetSdrClientApp.Networking
{
    public class UdpClientWrapper : IUdpClient, IDisposable
    {
        private readonly IPEndPoint _localEndPoint;
        private CancellationTokenSource? _cts;
        private UdpClient? _udpClient;
        private bool _disposed;

        public event EventHandler<byte[]>? MessageReceived;

        public UdpClientWrapper(int port)
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);
        }

        [ExcludeFromCodeCoverage]
        public async Task StartListeningAsync()
        {
            _cts = new CancellationTokenSource();
            Console.WriteLine("Start listening for UDP messages...");

            try
            {
                _udpClient = new UdpClient(_localEndPoint);
                while (!_cts.Token.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync(_cts.Token);
                    MessageReceived?.Invoke(this, result.Buffer);
                    Console.WriteLine($"Received from {result.RemoteEndPoint}");
                }
            }
            catch (OperationCanceledException)
            {
                // Очікувана подія при зупинці
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving message: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose(); //  Dispose для _cts
            }
        }

        [ExcludeFromCodeCoverage]
        public void StopListening()
        {
            SafeStop("Stopped listening for UDP messages.");
        }

        [ExcludeFromCodeCoverage]
        public void Exit()
        {
            SafeStop("Stopped listening for UDP messages.");
        }

        [ExcludeFromCodeCoverage]
        private void SafeStop(string message)
        {
            try
            {
                _cts?.Cancel();
                _udpClient?.Close();
                Console.WriteLine(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while stopping: {ex.Message}");
            }
        }

        public override int GetHashCode()
        {
            var payload = $"{nameof(UdpClientWrapper)}|{_localEndPoint.Address}|{_localEndPoint.Port}";
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToInt32(hash, 0);
        }

        public override bool Equals(object? obj)
        {
            if (obj is not UdpClientWrapper other)
                return false;

            return _localEndPoint.Address.Equals(other._localEndPoint.Address)
                   && _localEndPoint.Port == other._localEndPoint.Port;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cts?.Cancel();
            _cts?.Dispose();
            _udpClient?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}