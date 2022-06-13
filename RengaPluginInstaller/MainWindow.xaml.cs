using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Windows;
using Microsoft.Win32;
using System.ComponentModel;
using System.Net.Http;
using System.Reflection;

namespace RengaPluginInstaller
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 


    public partial class MainWindow : Window
    {
        Dictionary<string, string> arguments = new Dictionary<string, string>();

        const string _rengaInstallPath = "C:/Program Files/Renga";
        const string _rengaPluginsDirectoryName = "plugins";
        const string _pluginCachePath = "./tmp";
        const string _trustedSourceListURL = "http://rengacloud.ru/trusted_plugin_source.list";

        Dictionary<string, string> _requestParams;
        bool _isValidRequest = false;

        private void CloseWindow(object sender, RoutedEventArgs args)
        {
            this.Close();
        }


        public MainWindow()
        {
            InitializeComponent();
            string[] args = Environment.GetCommandLineArgs();

            string uriStr = "";
            for (int index = 1; index < args.Length; index++)
                uriStr += args[index];


            _requestParams = uriStr
                             .Replace("renplginst://", "")
                             .Split('&')
                             .Select(value => value.Split('='))
                             .ToDictionary(pair => pair[0], pair => WebUtility.UrlDecode(pair[1]).Replace("&amp;", "&"));

            if (_isValidRequest = CheckParams())
            {
                titleMessage.Text = String.Format("Установка плагина: {0}", _requestParams["name"]);
            }
            else
            {
                titleMessage.Text = String.Format("Установка невозможна");
                mainMessage.Text += String.Format("Неверные параметры запуска...");
                mainMessage.Text += String.Format("\nURI: {0}", uriStr);

                InstallButton.IsEnabled = false;
            }

        }

        private string GetRengaInstallDirectory()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\RengaCloud\\Renga Plugin Installer");
            if (key != null)
            {
                Object o = key.GetValue("RengaInstallPath");
                if (o != null)
                {
                    String rengaInstallPath = o as String;
                    return rengaInstallPath;
                }
            }

            throw new Exception(String.Format("Директория с установленной Renga не задана"));

        }

        private string GetRengaPluginsDirectory()
        {
            return GetRengaInstallDirectory() + System.IO.Path.DirectorySeparatorChar + _rengaPluginsDirectoryName;
        }

        private void PreparePluginInstallPath(object sender)
        {
            (sender as BackgroundWorker).ReportProgress(0, String.Format("Подготавлием папку для установки плагина..."));

            if (!Directory.Exists(_pluginCachePath))
                Directory.CreateDirectory(_pluginCachePath);

            if (!Directory.Exists(GetRengaInstallDirectory()))
                throw new Exception(String.Format("Директория с установленной Renga не существует. Полный путь: {0}", GetRengaInstallDirectory()));
            
            if (!Directory.Exists(GetRengaPluginsDirectory()))
                Directory.CreateDirectory(GetRengaPluginsDirectory());
        }

        private string GetAppName()
        {
            return Path.GetFileName(Assembly.GetEntryAssembly().GetName().Name);
        }

        private void PromtCredentials()
        {
            using (var dialog = new Ookii.Dialogs.Wpf.CredentialDialog())
            {
                // The window title will not be used on Vista and later; there the title will always be "Windows Security".
                dialog.WindowTitle = "Настройки";
                dialog.MainInstruction = "Введите имя пользователя и пароль.";
                dialog.Content = "Прокси сервер запрашивает имя пользователя и пароль.";
                dialog.ShowSaveCheckBox = true;
                dialog.ShowUIForSavedCredentials = true;
                // The target is the key under which the credentials will be stored.
                // It is recommended to set the target to something following the "Company_Application_Server" pattern.
                // Targets are per user, not per application, so using such a pattern will ensure uniqueness.
                dialog.Target = GetAppName();
                if (dialog.ShowDialog(this))
                {
                    dialog.ConfirmCredentials(true);                    
                }
            }
        }

        private void CheckProxy()
        {
            try
            {
                using (WebClient client = CreateWebClient())
                    client.DownloadData(new Uri("https://www.ya.ru"));
            }
            catch (Exception ex)
            {
                WebException we = ex as WebException;
                if (we != null && we.Response is HttpWebResponse)
                {
                    HttpWebResponse response = we.Response as HttpWebResponse;
                    if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                        PromtCredentials();
                }
            }
        }

        private WebClient CreateWebClient()
        {
            WebClient client = new WebClient();
            IWebProxy wp = WebRequest.DefaultWebProxy;
            wp.Credentials = CredentialCache.DefaultCredentials;

            var savedProxyCredentials = Ookii.Dialogs.Wpf.CredentialDialog.RetrieveCredential(GetAppName());
            if (savedProxyCredentials != null)
                wp.Credentials = savedProxyCredentials;

            client.Proxy = wp;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            return client;
        }

        private List<Uri> ParseSourceList(String filePath)
        {
            List<Uri> result = new List<Uri>();

            using (var file = System.IO.File.OpenText(filePath))
            {
                // Read file
                while (!file.EndOfStream)
                {
                    String line = file.ReadLine();

                    // Ignore empty lines
                    if (line.Length > 0)
                    {
                        Uri item = new Uri(line);

                        // Add to collection
                        result.Add(item);
                    }
                }
            }
            return result;
        }

        private void DownloadTrustedPluginSourceList(String filePath)
        {
            using (WebClient client = CreateWebClient())
                client.DownloadFile(_trustedSourceListURL, filePath);
        }

        private List<Uri> GetTrustedPluginSourceList()
        {
            var trustedSourceListFileName = Guid.NewGuid().ToString();
            List<Uri> result = new List<Uri>();

            try
            {
                DownloadTrustedPluginSourceList(trustedSourceListFileName);
                result = ParseSourceList(trustedSourceListFileName);
            }
            catch (Exception)
            {
            }

            RemoveFile(trustedSourceListFileName);

            if (result.Count() == 0)
                throw new Exception("Не удалось получить список доверенных источников плагинов.");

            return result;
        }

        private void DownloadPlugin(object sender, String pluginUrl, String tempZipPath)
        {
            (sender as BackgroundWorker).ReportProgress(0, String.Format("Скачиваем плагин..."));

            using (WebClient client = CreateWebClient())
                client.DownloadFile(pluginUrl, tempZipPath);
        }
        private void ExtractToRengaDirectory(object sender, String tempZipPath)
        {
            if (!File.Exists(tempZipPath))
            {
                throw new Exception(String.Format("Архив плагина не существует"));
            }

            (sender as BackgroundWorker).ReportProgress(0, String.Format("Устанавливаем плагин..."));
            
            ZipFile.ExtractToDirectory(tempZipPath, GetRengaPluginsDirectory());
        }

        private void RemoveFile(String tempZipPath)
        {
            try
            {
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);
            }
            catch(Exception)
            {
            }            
        }

        private void CheckPluginUrl(object sender, String pluginUrl)
        {
            (sender as BackgroundWorker).ReportProgress(0, String.Format("Проверяем ссылку на плагин..."));
            
            var trustedSourceList = GetTrustedPluginSourceList();

            try
            {
                Uri pluginUri = new Uri(pluginUrl);
                if (!trustedSourceList.Any(item => item.Host == pluginUri.Host))
                {
                    (sender as BackgroundWorker).ReportProgress(0, String.Format("Неизвестный источник плагина..."));
                    throw new Exception(); // Unknown plugin source
                }
                    
            }
            catch (Exception)
            {
                throw new Exception("Неправильная ссылка на плагин!");
            }
        }

        private void InstallPlugin(object sender, String pluginUrl)
        {
            var tempZipPath = _pluginCachePath + System.IO.Path.DirectorySeparatorChar + Guid.NewGuid().ToString() + ".zip";

            DownloadPlugin(sender, pluginUrl, tempZipPath);

            ExtractToRengaDirectory(sender, tempZipPath);

            RemoveFile(tempZipPath);
        }

        private bool CheckParams()
        {
            return _requestParams.ContainsKey("version") && _requestParams.ContainsKey("name") && _requestParams.ContainsKey("url");
        }

        public void TryToInstallPlugin(object sender, String pluginName, String pluginVersion, String pluginUrl)
        {
            CheckPluginUrl(sender, pluginUrl);
            {
                PreparePluginInstallPath(sender);
                {
                    InstallPlugin(sender, pluginUrl);
                }
            }
        }

        void InstallProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState != null)
                mainMessage.Text += "\n" + e.UserState;
        }

        void OnAsyncInstallCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            InstallButton.IsEnabled = false;
        }

        public void InstallButtonClick(object sender, RoutedEventArgs args)
        {
            CheckProxy();

            mainMessage.Text = String.Format("Начинаем установку...");

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += RunAsyncInstallProcess;
            worker.ProgressChanged += InstallProgressChanged;
            worker.RunWorkerCompleted += OnAsyncInstallCompleted;
            worker.RunWorkerAsync();
        }

        public void RunAsyncInstallProcess(object sender, DoWorkEventArgs e)
        {
            if (!_isValidRequest)
                return;

            try
            {
                var pluginUrl = _requestParams["url"];
                var pluginVersion = _requestParams["version"];
                var pluginName = _requestParams["name"];

                TryToInstallPlugin(sender, pluginName, pluginVersion, pluginUrl);
                (sender as BackgroundWorker).ReportProgress(0, String.Format("Плагин {0} успешно установлен !", pluginName));
            }
            catch (Exception ex)
            {
                (sender as BackgroundWorker).ReportProgress(0, String.Format("Не удалось установить плагин \n{0}", ex.Message));
            }

        }
    }
}

