# CreateVPN

Simple program to create a VPN using Digital Ocean.

Windows Only (tested on 10). Needs administrator for placing the file inside OpenVPN default client folder.

## Usage

Change the generated configuration file or use the following arguments when starting the program:

* "-DigitalOceanToken" or "-token": Digital Ocean Personal Access Token (https://cloud.digitalocean.com/settings/api/tokens)
* "-SshFingerprint" or "-fingerprint": Fingerprint for the ssh key created on Digital Ocean (https://cloud.digitalocean.com/settings/security)
* "-SshPrivateKeyFileName" or "-privatekey": File name with the private key for the key created on Digital Ocean (https://cloud.digitalocean.com/settings/security)
* "-SshPassphrase" or "-passphrase": Passphrase for the private key
* "-ClientFileFullName" or "-clientfile": Path and file name for the ovpn file (default "C:\Program Files\OpenVPN\config\client.ovpn")
* "-OpenVpnDirectory" or "-openvpn": Directory where you installed OpenVPN (default "C:\Program Files\OpenVPN\bin\")
