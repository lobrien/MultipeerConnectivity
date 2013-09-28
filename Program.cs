using System;
using MonoTouch.Foundation;
using MonoTouch.UIKit;
using System.Drawing;
using MonoTouch.MultipeerConnectivity;

namespace MultipeerSingleFile
{
	//Avoid needless subclassing
	public class TArgs<T> : EventArgs
	{
		public T Value { get; protected set; }

		public TArgs(T value)
		{
			this.Value = value;
		}
	}
	//One browser, many advertisers
	public enum Role
	{
		Advertiser,
		Browser
	}
	//Make messages easy to subscribe to
	public interface IMessager
	{
		event EventHandler<TArgs<string>> MessageReceived;
	}
	//Chat View
	public class ChatView : UIView
	{
		readonly UITextField message;
		readonly UIButton sendButton;
		readonly UILabel incoming;

		public ChatView(IMessager msgr)
		{
			BackgroundColor = UIColor.White;

			message = new UITextField(new RectangleF(10, 54, 100, 44)) {
				Placeholder = "Message"
			};
			AddSubview(message);

			sendButton = new UIButton(UIButtonType.System) {
				Frame = new RectangleF(220, 54, 50, 44)
			};
			sendButton.SetTitle("Send", UIControlState.Normal);
			AddSubview(sendButton);


			incoming = new UILabel(new RectangleF(10, 114, 100, 44));
			AddSubview(incoming);

			sendButton.TouchUpInside += (sender, e) => SendRequest(this, new TArgs<string>(message.Text));
			msgr.MessageReceived += (s, e) => incoming.Text = e.Value;
		}

		public event EventHandler<TArgs<string>> SendRequest = delegate {};
	}

	public class ChatViewController : UIViewController, IMessager
	{
		protected MCSession Session { get; private set; }

		protected MCPeerID Me { get; private set; }

		protected MCPeerID Them { get; private set; }

		ChatView cv;

		public ChatViewController(MCSession session, MCPeerID me, MCPeerID them, ChatSessionDelegate delObj) : base()
		{
			this.Session = session;
			this.Me = me;
			this.Them = them;

			delObj.ChatController = this;
		}

		public override void DidReceiveMemoryWarning()
		{
			// Releases the view if it doesn't have a superview.
			base.DidReceiveMemoryWarning();

			// Release any cached data, images, etc that aren't in use.
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();

			cv = new ChatView(this);
			View = cv;

			cv.SendRequest += (s, e) => {
				var msg = e.Value;
				var peers = Session.ConnectedPeers;
				NSError error = null;
				Session.SendData(NSData.FromString(msg), peers, MCSessionSendDataMode.Reliable, out error);
				if(error != null)
				{
					new UIAlertView("Error", error.ToString(), null, "OK", null).Show();
				}
			};
		}

		public void Message(string str)
		{
			MessageReceived(this, new TArgs<string>(str));
		}

		public event EventHandler<TArgs<string>> MessageReceived = delegate {};
	}

	//Base class for browser and advertiser view controllers
	public class DiscoveryViewController : UIViewController
	{
		public MCPeerID PeerID { get; private set; }

		public MCSession Session { get; private set; }

		protected const string SERVICE_STRING = "xam-chat";

		public DiscoveryViewController(string peerID) : base()
		{
			PeerID = new MCPeerID(peerID);
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();

			Session = new MCSession(PeerID);
			Session.Delegate = new ChatSessionDelegate(this);
		}

		public void Status(string str)
		{
			StatusChanged(this, new TArgs<string>(str));
		}

		public event EventHandler<TArgs<string>> StatusChanged;
	}

	public class ChatSessionDelegate : MCSessionDelegate
	{
		public DiscoveryViewController Parent{ get; protected set; }

		public ChatViewController ChatController
		{
			get; 
			set;
		}

		public ChatSessionDelegate(DiscoveryViewController parent)
		{
			Parent = parent;
		}

		public override void DidChangeState(MCSession session, MCPeerID peerID, MCSessionState state)
		{
			switch(state)
			{
			case MCSessionState.Connected:
				Console.WriteLine("Connected to " + peerID.DisplayName);
				InvokeOnMainThread(() => Parent.NavigationController.PushViewController(new ChatViewController(Parent.Session, Parent.PeerID, peerID, this), true));
				break;
			case MCSessionState.Connecting:
				Console.WriteLine("Connecting to " + peerID.DisplayName);
				break;
			case MCSessionState.NotConnected:
				Console.WriteLine("No longer connected to " + peerID.DisplayName);
				break;
			default:
				throw new ArgumentOutOfRangeException();
			}
		}

		public override void DidReceiveData(MCSession session, MonoTouch.Foundation.NSData data, MCPeerID peerID)
		{

			if(ChatController != null)
			{
				InvokeOnMainThread(() => ChatController.Message(String.Format("{0} : {1}", peerID.DisplayName, data.ToString())));
			}
		}

		public override void DidStartReceivingResource(MCSession session, string resourceName, MCPeerID fromPeer, MonoTouch.Foundation.NSProgress progress)
		{
			InvokeOnMainThread(() => new UIAlertView("Msg", "DidStartReceivingResource()", null, "OK", null).Show());

		}

		public override void DidFinishReceivingResource(MCSession session, string resourceName, MCPeerID formPeer, MonoTouch.Foundation.NSUrl localUrl, out MonoTouch.Foundation.NSError error)
		{
			InvokeOnMainThread(() => new UIAlertView("Msg", "DidFinishReceivingResource()", null, "OK", null).Show());
			error = null;

		}

		public override void DidReceiveStream(MCSession session, MonoTouch.Foundation.NSInputStream stream, string streamName, MCPeerID peerID)
		{
			InvokeOnMainThread(() => new UIAlertView("Msg", "DidReceiveStream()", null, "OK", null).Show());

		}
	}

	public class ReceivedEventArgs : EventArgs
	{
		public MCSession Session { get; protected set; }

		public NSData Data { get; protected set; }

		public MCPeerID Sender { get; protected set; }

		public ReceivedEventArgs(MCSession session, NSData data, MCPeerID sender)
		{
			this.Session = session;
			this.Data = data;
			this.Sender = sender;
		}
	}

	public class DiscoveryView : UIView
	{
		public DiscoveryView(string roleName, DiscoveryViewController controller)
		{
			BackgroundColor = UIColor.White;

			var roleLabel = new UILabel(new RectangleF(10, 64, 200, 44)) {
				Text = roleName
			};
			AddSubview(roleLabel);

			var statusLabel = new UILabel(new RectangleF(10, 114, 200, 44));
			AddSubview(statusLabel);

			controller.StatusChanged += (s, e) => statusLabel.Text = e.Value;
		}
	}

	public class AdvertiserController : DiscoveryViewController
	{
		MCNearbyServiceAdvertiser advertiser;

		public AdvertiserController(string peerID) : base(peerID)
		{
		}

		public override void DidReceiveMemoryWarning()
		{
			// Releases the view if it doesn't have a superview.
			base.DidReceiveMemoryWarning();

			// Release any cached data, images, etc that aren't in use.
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();

			View = new DiscoveryView("Advertiser", this);
			var emptyDict = new NSDictionary();
			Status("Starting advertising...");

			advertiser = new MCNearbyServiceAdvertiser(PeerID, emptyDict, SERVICE_STRING);
			advertiser.Delegate = new MyNearbyAdvertiserDelegate(this);
			advertiser.StartAdvertisingPeer();
		}
	}

	class MyNearbyAdvertiserDelegate : MCNearbyServiceAdvertiserDelegate
	{
		AdvertiserController parent;

		public MyNearbyAdvertiserDelegate(AdvertiserController parent)
		{
			this.parent = parent;
		}

		public override void DidReceiveInvitationFromPeer(MCNearbyServiceAdvertiser advertiser, MCPeerID peerID, NSData context, MCNearbyServiceAdvertiserInvitationHandler invitationHandler)
		{
			parent.Status("Received Invite");
			invitationHandler(true, parent.Session);
		}
	}

	public class BrowserController : DiscoveryViewController
	{
		MCNearbyServiceBrowser browser;

		public BrowserController(string peerID) : base(peerID)
		{
		}

		public override void DidReceiveMemoryWarning()
		{
			// Releases the view if it doesn't have a superview.
			base.DidReceiveMemoryWarning();

			// Release any cached data, images, etc that aren't in use.
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();

			View = new DiscoveryView("Browser", this);

			browser = new MCNearbyServiceBrowser(PeerID, SERVICE_STRING);
			browser.Delegate = new MyBrowserDelegate(this);

			Status("Starting browsing...");
			browser.StartBrowsingForPeers();
		}

		class MyBrowserDelegate : MCNearbyServiceBrowserDelegate
		{
			BrowserController parent;
			NSData context;

			public MyBrowserDelegate(BrowserController parent)
			{
				this.parent = parent;
				context = new NSData();
			}

			public override void FoundPeer(MCNearbyServiceBrowser browser, MCPeerID peerID, NSDictionary info)
			{
				parent.Status("Found peer " + peerID.DisplayName);
				browser.InvitePeer(peerID, parent.Session, context, 60);
			}

			public override void LostPeer(MCNearbyServiceBrowser browser, MCPeerID peerID)
			{
				parent.Status("Lost peer " + peerID.DisplayName);
			}

			public override void DidNotStartBrowsingForPeers(MCNearbyServiceBrowser browser, NSError error)
			{
				parent.Status("DidNotStartBrowingForPeers " + error.Description);
			}
		}
	}

	public class RoleSelectView : UIView
	{
		readonly UIButton advertiserButton;
		readonly UIButton browserButton;

		public RoleSelectView()
		{
			BackgroundColor = UIColor.White;

			var peerField = new UITextField(new RectangleF(10, 110, 200, 44)) {
				Placeholder = "PeerID"
			};
			AddSubview(peerField);

			advertiserButton = new UIButton(UIButtonType.System) {
				Frame = new RectangleF(10, 154, 200, 44),
				Enabled = false
			};
			advertiserButton.SetTitle("Advertiser", UIControlState.Normal);

			AddSubview(advertiserButton);
			browserButton = new UIButton(UIButtonType.System) {
				Frame = new RectangleF(10, 204, 200, 44),
				Enabled = false
			};
			browserButton.SetTitle("Browser", UIControlState.Normal);
			AddSubview(browserButton);

			peerField.EditingChanged += (sender, e) => PeerNameEdited(sender, new TArgs<String>(peerField.Text));
			advertiserButton.TouchUpInside += (sender, e) => RoleSelected(sender, new TArgs<Role>(Role.Advertiser));
			browserButton.TouchUpInside += (sender, e) => RoleSelected(sender, new TArgs<Role>(Role.Browser));
		}

		public void EnableRoles(bool shouldEnable)
		{
			advertiserButton.Enabled = shouldEnable;
			browserButton.Enabled = shouldEnable;
		}

		public event EventHandler<TArgs<String>> PeerNameEdited = delegate {};
		public event EventHandler<TArgs<Role>> RoleSelected = delegate {};
	}

	public class RoleSelectController : UIViewController
	{
		string peerID;

		public RoleSelectController() : base()
		{
		}

		public override void DidReceiveMemoryWarning()
		{
			// Releases the view if it doesn't have a superview.
			base.DidReceiveMemoryWarning();
		}

		public override void ViewDidLoad()
		{
			base.ViewDidLoad();

			var rsv = new RoleSelectView();
			View = rsv;

			rsv.PeerNameEdited += (s, e) => {
				peerID = e.Value;
				rsv.EnableRoles(peerID.Length > 0);
			};

			rsv.RoleSelected += (s, e) => {
				var role = e.Value;
				switch(role)
				{
				case Role.Advertiser: 
					NavigationController.PushViewController(new AdvertiserController(peerID), true);
					break;
				case Role.Browser:
					NavigationController.PushViewController(new BrowserController(peerID), true);
					break;
				}
			};
		}
	}

	[Register("AppDelegate")]
	public  class AppDelegate : UIApplicationDelegate
	{
		UIWindow window;

		public override bool FinishedLaunching(UIApplication app, NSDictionary options)
		{
			window = new UIWindow(UIScreen.MainScreen.Bounds);

			var rsc = new RoleSelectController();
			var nv = new UINavigationController(rsc);
			window.RootViewController = nv;

			window.MakeKeyAndVisible();

			return true;
		}
	}

	public class Application
	{
		static void Main(string[] args)
		{
			UIApplication.Main(args, null, "AppDelegate");
		}
	}
}

