using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static NetSdrClientApp.Messages.NetSdrMessageHelper;

namespace NetSdrClientApp
{
    public class NetSdrClient
    {
        private readonly ITcpClient _tcpClient;
        private readonly IUdpClient _udpClient;

        private TaskCompletionSource<byte[]>? responseTaskSource;

        public bool IQStarted { get; private set; }

        public NetSdrClient(ITcpClient tcpClient, IUdpClient udpClient)
        {
            _tcpClient = tcpClient;
            _udpClient = udpClient;

            _tcpClient.MessageReceived += _tcpClient_MessageReceived;
            _udpClient.MessageReceived += _udpClient_MessageReceived;
        }

        // ---------------------- CONNECTION ---------------------------------------------------

        public async Task ConnectAsync()
        {
            if (_tcpClient.Connected)
                return;

            _tcpClient.Connect();

            var sampleRate = BitConverter.GetBytes((long)100000).Take(5).ToArray();
            var automaticFilterMode = BitConverter.GetBytes((ushort)0).ToArray();
            var adMode = new byte[] { 0x00, 0x03 };

            var msgs = new List<byte[]>
            {
                GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.IQOutputDataSampleRate, sampleRate),
                GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.RFFilter, automaticFilterMode),
                GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ADModes, adMode),
            };

            foreach (var msg in msgs)
                await SendTcpRequest(msg);
        }

        public void Disconnect()
        {
            _tcpClient.Disconnect();
            _udpClient.StopListening();
            IQStarted = false;
        }

        // ---------------------- IQ CONTROL ---------------------------------------------------

        public async Task StartIQAsync()
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            var args = new byte[] { 0x80, 0x02, 0x01, 0x01 };

            var msg = GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverState, args);

            await SendTcpRequest(msg);

            IQStarted = true;
            _ = _udpClient.StartListeningAsync();
        }

        public async Task StopIQAsync()
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            var args = new byte[] { 0x00, 0x01, 0x00, 0x00 };

            var msg = GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverState, args);

            await SendTcpRequest(msg);

            IQStarted = false;
            _udpClient.StopListening();
        }

        // ---------------------- FREQUENCY ---------------------------------------------------

        public async Task ChangeFrequencyAsync(long hz, int channel)
        {
            var channelArg = (byte)channel;
            var freqBytes = BitConverter.GetBytes(hz).Take(5);
            var args = new[] { channelArg }.Concat(freqBytes).ToArray();

            var msg = GetControlItemMessage(MsgTypes.SetControlItem, ControlItemCodes.ReceiverFrequency, args);

            await SendTcpRequest(msg);
        }

        // ---------------------- UDP MESSAGE HANDLER -----------------------------------------

        private void _udpClient_MessageReceived(object? sender, byte[] e)
        {
            TranslateMessage(e, out _, out _, out _, out byte[] body);

            var samples = GetSamples(16, body);

            Console.WriteLine("Samples received: " +
                string.Join(" ", body.Select(b => b.ToString("X2"))));

            using var fs = new FileStream("samples.bin", FileMode.Append, FileAccess.Write, FileShare.Read);
            using var bw = new BinaryWriter(fs);

            foreach (var sample in samples)
                bw.Write((short)sample);
        }

        // ---------------------- TCP REQUEST / RESPONSE ---------------------------------------

        private async Task<byte[]?> SendTcpRequest(byte[] msg)
        {
            if (!_tcpClient.Connected)
            {
                Console.WriteLine("No active connection.");
                return null;
            }

            responseTaskSource = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            await _tcpClient.SendMessageAsync(msg);

            return await responseTaskSource.Task;
        }

        private void _tcpClient_MessageReceived(object? sender, byte[] e)
        {
            if (responseTaskSource != null)
            {
                responseTaskSource.TrySetResult(e);
                responseTaskSource = null;
            }

            Console.WriteLine("Response received: " +
                string.Join(" ", e.Select(b => b.ToString("X2"))));
        }
    }
}
