using System.Collections.ObjectModel;
using Backend.Cam;
using Backend.Models;
using Backend.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

/// <summary>
/// Observable wrapper around CamSettings + ProfileStore.
/// CutSettingsPanel binds to this directly via MainViewModel.CutSettings.
/// </summary>
public sealed class CutSettingsViewModel : ObservableObject
{
    private readonly ProjectService _projects;
    private readonly ProfileStore   _profileStore;

    // ── Current settings ──────────────────────────────────────────────────

    private CamSettings _settings = new();
    public CamSettings Settings
    {
        get => _settings;
        private set
        {
            SetProperty(ref _settings, value);
            OnPropertyChanged(nameof(IsPlasma));
            OnPropertyChanged(nameof(IsLaser));
            OnPropertyChanged(nameof(IsVinyl));
        }
    }

    public bool IsPlasma => Settings.OperationMode == MachineType.Plasma;
    public bool IsLaser  => Settings.OperationMode == MachineType.Laser;
    public bool IsVinyl  => Settings.OperationMode == MachineType.VinylKnife;

    // ── Material profiles ─────────────────────────────────────────────────

    public ObservableCollection<MaterialProfile> Profiles { get; } = [];

    private Guid? _selectedProfileId;
    public Guid? SelectedProfileId
    {
        get => _selectedProfileId;
        set => SetProperty(ref _selectedProfileId, value);
    }

    public CutSettingsViewModel(ProjectService projects, ProfileStore profileStore)
    {
        _projects     = projects;
        _profileStore = profileStore;
        Reload();
    }

    // ── Sync ─────────────────────────────────────────────────────────────

    public void Reload()
    {
        var cam = _projects.With(p => p.Cam);
        Settings = CopyCam(cam);

        Profiles.Clear();
        foreach (var p in _profileStore.All()) Profiles.Add(p);
    }

    // ── Mutations (UI calls these) ────────────────────────────────────────

    public void SaveSettings(CamSettings updated)
    {
        _projects.Mutate(p =>
        {
            // Copy all fields so the reference on the Project stays stable.
            CopyInto(updated, p.Cam);
        });
        Settings = CopyCam(updated);
    }

    public void ApplyProfile(Guid profileId)
    {
        var p = _profileStore.Find(profileId);
        if (p is null) return;
        var next = CopyCam(Settings);
        next.KerfWidthMm    = p.KerfWidthMm;
        next.FeedRateMmMin  = p.FeedRateMmMin;
        next.PierceDelayS   = p.PierceDelayS;
        next.CutHeightMm    = p.CutHeightMm;
        next.PierceHeightMm = p.PierceHeightMm;
        SaveSettings(next);
        SelectedProfileId = profileId;
    }

    public MaterialProfile SaveAsProfile(string name)
    {
        var p = _profileStore.Add(new MaterialProfile
        {
            Name           = name,
            Material       = "",
            ThicknessMm    = 0,
            KerfWidthMm    = Settings.KerfWidthMm,
            FeedRateMmMin  = Settings.FeedRateMmMin,
            PierceDelayS   = Settings.PierceDelayS,
            CutHeightMm    = Settings.CutHeightMm,
            PierceHeightMm = Settings.PierceHeightMm,
        });
        Profiles.Add(p);
        SelectedProfileId = p.Id;
        return p;
    }

    public bool UpdateSelectedProfile()
    {
        if (SelectedProfileId is not { } id) return false;
        var p = Profiles.FirstOrDefault(x => x.Id == id);
        if (p is null) return false;
        p.KerfWidthMm    = Settings.KerfWidthMm;
        p.FeedRateMmMin  = Settings.FeedRateMmMin;
        p.PierceDelayS   = Settings.PierceDelayS;
        p.CutHeightMm    = Settings.CutHeightMm;
        p.PierceHeightMm = Settings.PierceHeightMm;
        return _profileStore.Update(p);
    }

    public bool DeleteSelectedProfile()
    {
        if (SelectedProfileId is not { } id) return false;
        if (!_profileStore.Delete(id)) return false;
        var item = Profiles.FirstOrDefault(x => x.Id == id);
        if (item is not null) Profiles.Remove(item);
        SelectedProfileId = null;
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static CamSettings CopyCam(CamSettings src) => new()
    {
        OperationMode      = src.OperationMode,
        FeedRateMmMin      = src.FeedRateMmMin,
        KerfWidthMm        = src.KerfWidthMm,
        PierceDelayS       = src.PierceDelayS,
        CutHeightMm        = src.CutHeightMm,
        PierceHeightMm     = src.PierceHeightMm,
        LeadInType         = src.LeadInType,
        LeadInLengthMm     = src.LeadInLengthMm,
        LeadOutType        = src.LeadOutType,
        LeadOutLengthMm    = src.LeadOutLengthMm,
        LaserPowerPercent  = src.LaserPowerPercent,
        VinylBladeOffsetMm = src.VinylBladeOffsetMm,
        VinylOvercutMm     = src.VinylOvercutMm,
        VinylKnifeUpMm     = src.VinylKnifeUpMm,
        VinylKnifeDownMm   = src.VinylKnifeDownMm,
    };

    private static void CopyInto(CamSettings src, CamSettings dst)
    {
        dst.OperationMode      = src.OperationMode;
        dst.FeedRateMmMin      = src.FeedRateMmMin;
        dst.KerfWidthMm        = src.KerfWidthMm;
        dst.PierceDelayS       = src.PierceDelayS;
        dst.CutHeightMm        = src.CutHeightMm;
        dst.PierceHeightMm     = src.PierceHeightMm;
        dst.LeadInType         = src.LeadInType;
        dst.LeadInLengthMm     = src.LeadInLengthMm;
        dst.LeadOutType        = src.LeadOutType;
        dst.LeadOutLengthMm    = src.LeadOutLengthMm;
        dst.LaserPowerPercent  = src.LaserPowerPercent;
        dst.VinylBladeOffsetMm = src.VinylBladeOffsetMm;
        dst.VinylOvercutMm     = src.VinylOvercutMm;
        dst.VinylKnifeUpMm     = src.VinylKnifeUpMm;
        dst.VinylKnifeDownMm   = src.VinylKnifeDownMm;
    }
}
