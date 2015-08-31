using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;
using Universal.Torrent.Client;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Settings;
using Universal.Torrent.Dht;
using Universal.Torrent.Dht.Listeners;

namespace Universal.Torrent.Example
{
    public sealed partial class MainPage
    {
        public MainPage()
        {
            InitializeComponent();
        }


        protected override async void OnNavigatedTo(NavigationEventArgs ee)
        {
            var port = 6881;
            var dhtPort = 15000;

            // Use Universal.Nat to enable upnp port mapping
            /* var natManager = new NatManager(port);
            natManager.Start();*/

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".torrent");
            var file = await picker.PickSingleFileAsync();
            var stream = await file.OpenStreamForReadAsync();

            var torrent = Common.Torrent.Load(stream);
            if (torrent != null)
            {
                var engineSettings = new EngineSettings(ApplicationData.Current.LocalFolder.Path, port)
                {
                    PreferEncryption = true,
                    AllowedEncryption = EncryptionTypes.All
                };

                // Create the default settings which a torrent will have.
                // 4 Upload slots - a good ratio is one slot per 5kB of upload speed
                // 50 open connections - should never really need to be changed
                // Unlimited download speed - valid range from 0 -> int.Max
                // Unlimited upload speed - valid range from 0 -> int.Max
                var torrentDefaults = new TorrentSettings(4, 150, 0, 0)
                {
                    UseDht = true,
                    EnablePeerExchange = true
                };

                // Create an instance of the engine.
                var engine = new ClientEngine(engineSettings);
                //engine.ChangeListenEndpoint(new IPEndPoint(IPAddress.Any, port));

                var dhtListner = new DhtListener(new IPEndPoint(IPAddress.Any, dhtPort));
                var dht = new DhtEngine(dhtListner);
                engine.RegisterDht(dht);
                dhtListner.Start();
                engine.DhtEngine.Start();

                // When any preprocessing has been completed, you create a TorrentManager
                // which you then register with the engine.
                var manager = new TorrentManager(torrent, ApplicationData.Current.LocalFolder, torrentDefaults);
                engine.Register(manager);

                // Every time a piece is hashed, this is fired.
                manager.PieceHashed +=
                    delegate(object o, PieceHashedEventArgs e)
                    {
                        Debug.WriteLine("Piece Hashed: {0} - {1}", e.PieceIndex, e.HashPassed ? "Pass" : "Fail");
                    };

                // Every time the state changes (Stopped -> Seeding -> Downloading -> Hashing) this is fired
                manager.TorrentStateChanged +=
                    delegate(object o, TorrentStateChangedEventArgs e)
                    {
                        Debug.WriteLine("OldState: " + e.OldState + " NewState: " + e.NewState);
                    };

                // Every time the tracker's state changes, this is fired
                foreach (var t in manager.TrackerManager.SelectMany(tier => tier.Trackers))
                {
                    t.AnnounceComplete +=
                        delegate(object sender, AnnounceResponseEventArgs e)
                        {
                            Debug.WriteLine("{0}: {1}", e.Successful, e.Tracker);
                        };
                }
                // Start the torrentmanager. The file will then hash (if required) and begin downloading/seeding
                manager.Start();

                var dispatcher = Window.Current.Dispatcher;
                engine.StatsUpdate +=
                    async (sender, args) =>
                    {
                        await
                            dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                                () =>
                                {
                                    TextBlock.Text =
                                        $"{manager.Peers.Seeds} seeds / {manager.Peers.Leechs} leechs / {manager.Progress} %";
                                });
                    };
            }
        }
    }
}