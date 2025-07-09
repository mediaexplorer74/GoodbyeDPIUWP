using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;

namespace GoodbyeDPIUWP
{
    public class ProxyServer
    {
        private StreamSocketListener listener;
        private TextBox logBox;
        public ProxyServer(TextBox logBox)
        {
            this.logBox = logBox;
        }
        public async void Start(ushort port)
        {
            listener = new StreamSocketListener();
            listener.Control.KeepAlive = true;
            listener.ConnectionReceived += OnConnectionReceived;
            try
            {
                await listener.BindServiceNameAsync(port.ToString());
                Log($"Прокси-сервер слушает порт {port}");
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска прокси: {ex.Message}");
            }
        }
        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            Log($"Новое подключение: {args.Socket.Information.RemoteAddress}");
            try
            {
                var socket = args.Socket;
                var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                uint bytesRead = await reader.LoadAsync(1024);
                if (bytesRead == 0)
                {
                    Log("Пустой запрос");
                    return;
                }
                byte[] buffer = new byte[bytesRead];
                reader.ReadBytes(buffer);
                string request = Encoding.UTF8.GetString(buffer);
                Log($"Запрос: {request.Split('\n')[0]}");
                
                // --- HTTPS CONNECT support ---
                if (request.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = request.Split(' ');
                    if (parts.Length < 2)
                    {
                        Log("Некорректный CONNECT-запрос");
                        return;
                    }
                    string target = parts[1]; // example.com:443
                    string[] hostPort = target.Split(':');
                    if (hostPort.Length != 2)
                    {
                        Log("Некорректный CONNECT host:port");
                        return;
                    }
                    string host = hostPort[0];
                    string port = hostPort[1];
                    Log($"CONNECT к {host}:{port}");
                    var remoteSocket = new StreamSocket();
                    try
                    {
                        await remoteSocket.ConnectAsync(new HostName(host), port);
                        // Ответ клиенту: 200 OK
                        var clientWriter = new DataWriter(socket.OutputStream);
                        string response = "HTTP/1.1 200 Connection Established\r\n\r\n";
                        clientWriter.WriteString(response);
                        await clientWriter.StoreAsync();
                        // Туннелирование байтов в обе стороны
                        _ = TunnelDataAsync(socket, remoteSocket);
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка CONNECT: {ex.Message}");
                    }
                    return;
                }

                // --- DPI bypass: меняем регистр Host на HoSt ---
                string modifiedRequest = request.Replace("Host:", "HoSt:");
                byte[] modifiedBuffer = Encoding.UTF8.GetBytes(modifiedRequest);

                // Поиск Host для HTTP
                string hostHeader = null;
                using (var sr = new StringReader(request))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                        {
                            hostHeader = line.Substring(5).Trim();
                            break;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(hostHeader))
                {
                    Log("Host не найден, пропуск");
                    return;
                }
                var remoteSocketHttp = new StreamSocket();
                try
                {
                    await remoteSocketHttp.ConnectAsync(new HostName(hostHeader), "80");
                    var remoteWriter = new DataWriter(remoteSocketHttp.OutputStream);
                    remoteWriter.WriteBytes(modifiedBuffer);
                    await remoteWriter.StoreAsync();
                    // Чтение ответа
                    var responseReader = new DataReader(remoteSocketHttp.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                    uint respBytes = await responseReader.LoadAsync(4096);
                    if (respBytes == 0) return;
                    byte[] respBuffer = new byte[respBytes];
                    responseReader.ReadBytes(respBuffer);
                    // Отправка клиенту
                    var clientWriter = new DataWriter(socket.OutputStream);
                    clientWriter.WriteBytes(respBuffer);
                    await clientWriter.StoreAsync();
                }
                catch (Exception ex)
                {
                    Log($"Ошибка соединения с сервером: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка чтения запроса: {ex.Message}");
            }
        }

        // Туннелирование данных между клиентом и сервером (для CONNECT/HTTPS)
        private async Task TunnelDataAsync(StreamSocket client, StreamSocket remote)
        {
            var clientToRemote = TunnelOneWayAsync(client.InputStream, remote.OutputStream, "C->S");
            var remoteToClient = TunnelOneWayAsync(remote.InputStream, client.OutputStream, "S->C");
            await Task.WhenAny(clientToRemote, remoteToClient);
            client.Dispose();
            remote.Dispose();
        }

        private async Task TunnelOneWayAsync(IInputStream input, IOutputStream output, string direction)
        {
            try
            {
                var reader = new DataReader(input) { InputStreamOptions = InputStreamOptions.Partial };
                var writer = new DataWriter(output);
                while (true)
                {
                    uint bytesRead = await reader.LoadAsync(1024);
                    if (bytesRead == 0) break;
                    byte[] buffer = new byte[bytesRead];
                    reader.ReadBytes(buffer);
                    writer.WriteBytes(buffer);
                    await writer.StoreAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"Туннель {direction} завершён: {ex.Message}");
            }
        }
        private async void Log(string text)
        {
            await logBox.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                logBox.Text += text + "\n";
            });
        }
    }
}
