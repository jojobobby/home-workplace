using HomeWorkplace.Client;

namespace HomeWorkplace.App;

public partial class App : Application
{
	private readonly ServiceSupervisor _supervisor;

	public App(ServiceSupervisor supervisor)
	{
		InitializeComponent();
		_supervisor = supervisor;
		MainPage = new MainPage();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = base.CreateWindow(activationState);
		window.Title = "Home Workplace";
		window.Destroying += (_, _) => _supervisor.Stop();   // closing the app sends the company home
		return window;
	}
}
