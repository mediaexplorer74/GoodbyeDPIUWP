using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace GoodbyeDPIUWP
{
    public sealed partial class MainPage : Page
    {
        private ProxyServer proxyServer = null;
        public MainPage()
        {
            this.InitializeComponent();
        }
        private void StartProxyButton_Click(object sender, RoutedEventArgs e)
        {
            if (proxyServer == null)
            {
                proxyServer = new ProxyServer(LogTextBox);
                proxyServer.Start(8888);
                StatusText.Text = "Прокси запущен на порту 8888";
                Log("Прокси-сервер запущен. Ожидание подключений...");
            }
            else
            {
                Log("Прокси уже запущен");
            }
        }
        public void Log(string text)
        {
            LogTextBox.Text += text + "\n";
        }
    }
}
