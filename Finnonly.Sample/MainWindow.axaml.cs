using Avalonia.Controls;

namespace Finnonly.Sample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 启动时测试 CopyTo / Compare 源生成器
        if (DataContext is MainViewModel vm)
            vm.TestCopyToCompare();
    }
}
