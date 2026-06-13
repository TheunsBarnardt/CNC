using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Cam;
using Backend.Models;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class CutSettingsPanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private bool _loading;

    public CutSettingsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => LoadFromVm();
    }

    private void LoadFromVm()
    {
        if (Vm?.CutSettings.Settings is not { } s) return;
        _loading = true;
        TbFeed.Text         = s.FeedRateMmMin.ToString("F1");
        TbKerf.Text         = s.KerfWidthMm.ToString("F2");
        TbPierceDelay.Text  = s.PierceDelayS.ToString("F2");
        TbCutHeight.Text    = s.CutHeightMm.ToString("F2");
        TbPierceHeight.Text = s.PierceHeightMm.ToString("F2");
        TbLaserPower.Text   = s.LaserPowerPercent.ToString("F0");
        TbLeadInLen.Text    = s.LeadInLengthMm.ToString("F1");
        TbLeadOutLen.Text   = s.LeadOutLengthMm.ToString("F1");
        TbBladeOffset.Text  = s.VinylBladeOffsetMm.ToString("F2");
        TbOvercut.Text      = s.VinylOvercutMm.ToString("F2");
        TbKnifeUp.Text      = s.VinylKnifeUpMm.ToString("F2");
        TbKnifeDown.Text    = s.VinylKnifeDownMm.ToString("F2");
        SelectComboByTag(CbLeadIn,  s.LeadInType.ToString());
        SelectComboByTag(CbLeadOut, s.LeadOutType.ToString());
        _loading = false;
    }

    private void OnModeChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm is null) return;
        var mode = RbPlasma.IsChecked == true ? MachineType.Plasma
                 : RbLaser.IsChecked  == true ? MachineType.Laser
                 : MachineType.VinylKnife;
        var next = Clone(Vm.CutSettings.Settings);
        next.OperationMode = mode;
        Vm.CutSettings.SaveSettings(next);
    }

    private void OnParamChanged(object? s, RoutedEventArgs e) => CommitFields();
    private void OnParamChanged(object? s, SelectionChangedEventArgs e) => CommitFields();

    private void CommitFields()
    {
        if (_loading || Vm is null) return;
        var src  = Vm.CutSettings.Settings;
        var next = Clone(src);
        if (double.TryParse(TbFeed.Text,         out var v)) next.FeedRateMmMin      = v;
        if (double.TryParse(TbKerf.Text,         out v))     next.KerfWidthMm        = v;
        if (double.TryParse(TbPierceDelay.Text,  out v))     next.PierceDelayS       = v;
        if (double.TryParse(TbCutHeight.Text,    out v))     next.CutHeightMm        = v;
        if (double.TryParse(TbPierceHeight.Text, out v))     next.PierceHeightMm     = v;
        if (double.TryParse(TbLaserPower.Text,   out v))     next.LaserPowerPercent  = v;
        if (double.TryParse(TbLeadInLen.Text,    out v))     next.LeadInLengthMm     = v;
        if (double.TryParse(TbLeadOutLen.Text,   out v))     next.LeadOutLengthMm    = v;
        if (double.TryParse(TbBladeOffset.Text,  out v))     next.VinylBladeOffsetMm = v;
        if (double.TryParse(TbOvercut.Text,      out v))     next.VinylOvercutMm     = v;
        if (double.TryParse(TbKnifeUp.Text,      out v))     next.VinylKnifeUpMm     = v;
        if (double.TryParse(TbKnifeDown.Text,    out v))     next.VinylKnifeDownMm   = v;
        next.LeadInType  = ParseLeadType(CbLeadIn);
        next.LeadOutType = ParseLeadType(CbLeadOut);
        Vm.CutSettings.SaveSettings(next);
    }

    private void OnProfileSelected(object? s, SelectionChangedEventArgs e) { }

    private void OnApplyProfile(object? s, RoutedEventArgs e)
    {
        if (Vm is null || CbProfile.SelectedItem is not MaterialProfile p) return;
        Vm.CutSettings.ApplyProfile(p.Id);
        LoadFromVm();
    }

    private void OnUpdateProfile(object? s, RoutedEventArgs e)
    {
        Vm?.CutSettings.UpdateSelectedProfile();
    }

    private void OnDeleteProfile(object? s, RoutedEventArgs e)
    {
        Vm?.CutSettings.DeleteSelectedProfile();
    }

    private void OnSaveAsProfile(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var name = TbNewProfileName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        Vm.CutSettings.SaveAsProfile(name);
        TbNewProfileName.Text = "";
    }

    private static LeadType ParseLeadType(ComboBox cb) =>
        cb.SelectedItem is ComboBoxItem item && Enum.TryParse<LeadType>(item.Tag as string, out var t) ? t : LeadType.None;

    private static void SelectComboByTag(ComboBox cb, string tag)
    {
        foreach (var i in cb.Items.OfType<ComboBoxItem>())
            if (string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            { cb.SelectedItem = i; return; }
    }

    private static CamSettings Clone(CamSettings s) => new()
    {
        OperationMode      = s.OperationMode,
        FeedRateMmMin      = s.FeedRateMmMin,
        KerfWidthMm        = s.KerfWidthMm,
        PierceDelayS       = s.PierceDelayS,
        CutHeightMm        = s.CutHeightMm,
        PierceHeightMm     = s.PierceHeightMm,
        LeadInType         = s.LeadInType,
        LeadInLengthMm     = s.LeadInLengthMm,
        LeadOutType        = s.LeadOutType,
        LeadOutLengthMm    = s.LeadOutLengthMm,
        LaserPowerPercent  = s.LaserPowerPercent,
        VinylBladeOffsetMm = s.VinylBladeOffsetMm,
        VinylOvercutMm     = s.VinylOvercutMm,
        VinylKnifeUpMm     = s.VinylKnifeUpMm,
        VinylKnifeDownMm   = s.VinylKnifeDownMm,
    };
}
