using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Renci.SshNet;
using RestSharp;

namespace CreateVPN
{
    class Program
    {
        private static string _digitalOceanToken;
        private static string DigitalOceanToken
        {
            get { return _digitalOceanToken ?? ConfigurationManager.AppSettings["DigitalOceanToken"]; }
            set { _digitalOceanToken = value; }
        }

        private static string _sshFingerprint;
        private static string SshFingerprint
        {
            get { return _sshFingerprint ?? ConfigurationManager.AppSettings["SshFingerprint"]; }
            set { _sshFingerprint = value; }
        }

        private static string _sshPassphrase;
        private static string SshPassphrase
        {
            get { return _sshPassphrase ?? ConfigurationManager.AppSettings["SshPassphrase"]; }
            set { _sshPassphrase = value; }
        }

        private static string _sshPrivateKeyFileName;
        private static string SshPrivateKeyFileName
        {
            get { return _sshPrivateKeyFileName ?? ConfigurationManager.AppSettings["SshPrivateKeyFileName"]; }
            set { _sshPrivateKeyFileName = value; }
        }

        private static string _clientFileFullName;
        private static string ClientFileFullName
        {
            get { return _clientFileFullName ?? ConfigurationManager.AppSettings["ClientFileFullName"]; }
            set { _clientFileFullName = value; }
        }

        private static string _openVpnDirectory;
        private static string OpenVpnDirectory
        {
            get { return _openVpnDirectory ?? ConfigurationManager.AppSettings["OpenVpnDirectory"]; }
            set { _openVpnDirectory = value; }
        }

        static void Main(string[] args)
        {
            Process process = null;

            CheckArgs(args);

            var droplet = CreateDroplet();

            try
            {
                DownloadAndCopyFile(droplet.Networks.V4[0].Ip_Address);

                process = RunOvpn();

                WaitForCommand();
            }
            catch (Exception ex)
            {
                Console.WriteLine(@"Error: " + ex.Message);
                Console.WriteLine(@"Press any button to continue");

                Console.ReadKey();
            }

            StopProcess(process);

            DestroyDroplet(droplet.Id);
        }

        private static void CheckArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-DigitalOceanToken":
                    case "-token":
                        DigitalOceanToken = args[i + 1];
                        break;
                    case "-SshFingerprint":
                    case "-fingerprint":
                        SshFingerprint = args[i + 1];
                        break;
                    case "-SshPassphrase":
                    case "-passphrase":
                        SshPassphrase = args[i + 1];
                        break;
                    case "-SshPrivateKeyFileName":
                    case "-privatekey":
                        SshPrivateKeyFileName = args[i + 1];
                        break;
                    case "-ClientFileFullName":
                    case "-clientfile":
                        ClientFileFullName = args[i + 1];
                        break;
                    case "-OpenVpnDirectory":
                    case "-openvpn":
                        OpenVpnDirectory = args[i + 1];
                        break;
                    default:
                        continue;
                }
            }
        }

        private static void StopProcess(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            process.Kill();
        }

        private static Droplet CreateDroplet()
        {
            Console.WriteLine(@"-Creating Droplet");

            var client = new RestClient("https://api.digitalocean.com/v2");

            var createRequest = new RestRequest("droplets");
            createRequest.AddHeader("Content-Type", "application/json");
            createRequest.AddHeader("Authorization", "Bearer " + DigitalOceanToken);
            createRequest.AddJsonBody(new
            {
                name = "auto-gen-vpn-" + Guid.NewGuid(),
                region = "nyc3",
                size = "512mb",
                image = "ubuntu-14-04-x64",
                ssh_keys = new[] { SshFingerprint },
                backups = false,
                ipv6 = true,
                user_data = Resource.UserData,
                private_networking = false
            });

            var createResponse = client.Post<CreateResponse>(createRequest);
            if (createResponse.StatusCode != HttpStatusCode.Accepted)
            {
                throw new ApplicationException("--Could not create the droplet");
            }

            Console.WriteLine(@"-Droplet created");

            Console.WriteLine(@"-Getting Droplet Data");

            int count = 1;
            IRestResponse<CreateResponse> getResponse;
            do
            {
                Console.WriteLine("--" + count + " Try");
                Thread.Sleep(12000);

                getResponse = GetDroplet(createResponse.Data.Droplet.Id, client);

                if (getResponse.StatusCode != HttpStatusCode.OK)
                {
                    break;
                }

                ++count;
            } while (getResponse.Data.Droplet.Status != "active");

            var ip = getResponse.Data?.Droplet.Networks?.V4?[0].Ip_Address;
            if (string.IsNullOrEmpty(ip))
            {
                throw new ApplicationException("--Could not get the IPv4 from the droplet");
            }

            Console.WriteLine(@"-Got Droplet Data");

            return getResponse.Data.Droplet;
        }

        private static IRestResponse<CreateResponse> GetDroplet(long id, IRestClient client)
        {
            var getRequest = new RestRequest("droplets/" + id);
            getRequest.AddHeader("Content-Type", "application/json");
            getRequest.AddHeader("Authorization", "Bearer " + DigitalOceanToken);

            var getResponse = client.Get<CreateResponse>(getRequest);

            return getResponse;
        }

        private static void DownloadAndCopyFile(string ip)
        {
            var client = new SftpClient(ip, 22, "root", new PrivateKeyFile(SshPrivateKeyFileName, SshPassphrase));

            Console.WriteLine(@"-Connecting to server");

            int count = 0;
            bool ok = false;
            do
            {
                try
                {
                    Console.WriteLine("--" + (count + 1) + @" try");

                    Thread.Sleep(10000);

                    client.Connect();
                    ok = true;
                }
                catch
                {
                    ++count;
                }
            } while (!ok && count < 5);

            if (!ok)
            {
                throw new ApplicationException("Could not connect to the droplet");
            }

            Console.WriteLine(@"-Connected to server to server");
            Console.WriteLine(@"-Downloading ovpn file (one try a minute, max 20 tries; can take a few minutes to create the file on the server)");

            Console.WriteLine(@"--Check file existence");

            count = 0;
            ok = false;
            do
            {
                Console.WriteLine(@"---" + (count + 1) + @" try");

                var files = client.ListDirectory("/root/");

                ok = files.Any(f => f.Name == "client.ovpn");

                if (!ok)
                {
                    Console.WriteLine(@"----File still not created, waiting...");

                    Thread.Sleep(60000);
                    ++count;
                }
            } while (!ok && count < 20);

            if (!ok)
            {
                throw new ApplicationException("Could not download the ovpn file from the droplet");
            }

            Console.WriteLine(@"--File exist, Downloading...");

            if (File.Exists(ClientFileFullName))
                File.Delete(ClientFileFullName);

            var stream = File.OpenWrite(ClientFileFullName);

            client.DownloadFile("/root/client.ovpn", stream);

            stream.Flush(true);
            stream.Close();

            Console.WriteLine(@"-Downloaded ovpn file");
        }

        private static Process RunOvpn()
        {
            Console.WriteLine(@"-Starting openvpn process (wait until 'Initialization Sequence Completed' appears to start using)");

            var startInfo = new ProcessStartInfo("\"" + OpenVpnDirectory + "openvpn.exe\"",
                "\"" + ClientFileFullName + "\"")
            {
                Verb = "runas"
            };

            return Process.Start(startInfo);
        }

        private static void WaitForCommand()
        {
            Console.Write(@"Type 'quit' and press enter when you finish (will destroy the droplet): ");

            Console.ReadLine();
        }

        private static void DestroyDroplet(long id)
        {
            Console.WriteLine(@"-Destroying the droplet");

            var client = new RestClient("https://api.digitalocean.com/v2");

            var request = new RestRequest("droplets/" + id);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + DigitalOceanToken);

            var response = client.Delete(request);
            if (response.StatusCode != HttpStatusCode.NoContent)
            {
                throw new ApplicationException("Could not delete the droplet");
            }

            Console.WriteLine(@"-Droplet destroyed");
        }

        #region HelperClasses

        class CreateResponse
        {
            public Droplet Droplet { get; set; }
        }

        class Droplet
        {
            public long Id { get; set; }
            public string Status { get; set; }
            public Network Networks { get; set; }
        }

        class Network
        {
            public List<NetworkImp> V4 { get; set; }
            public List<NetworkImp> V6 { get; set; }
        }

        class NetworkImp
        {
            public string Ip_Address { get; set; }
        }

        #endregion
    }
}
