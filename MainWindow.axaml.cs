using Avalonia.Controls;
using NexLauncher.Services;
using NexLauncher.ViewModels;

namespace NexLauncher;

public partial class MainWindow : Window
{
    public MainWindow() : this(CreateViewModel()) { }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.InitializeAsync();
        Closing += (_, _) => viewModel.OnWindowClosing();
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var store = new ConfigurationStore();
        return new MainWindowViewModel(store, new MinecraftService(store.DataDirectory), new MicrosoftAccountService());
    }
}
