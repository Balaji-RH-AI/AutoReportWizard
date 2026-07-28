using System;
using System.Windows;
using System.Windows.Controls;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views;

public sealed partial class Step1View : UserControl
{
    public Step1View()
    {
        InitializeComponent();
    }

    private void UserPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);

        if (sender is not PasswordBox passwordBox)
            return;

        if (DataContext is not WizardViewModel viewModel)
            return;

        viewModel.Password = passwordBox.Password;
    }
}