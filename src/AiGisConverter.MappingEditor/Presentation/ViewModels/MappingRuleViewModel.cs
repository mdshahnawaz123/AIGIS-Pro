using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AiGisConverter.Business.Classification;

namespace AiGisConverter.MappingEditor.Presentation.ViewModels;

public class MappingRuleViewModel : INotifyPropertyChanged
{
    private readonly MappingRule _rule;
    public MappingRule Model => _rule;

    public MappingRuleViewModel(MappingRule rule)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
    }

    public string RuleName
    {
        get => _rule.RuleName;
        set { if (_rule.RuleName != value) { _rule.RuleName = value; OnPropertyChanged(); } }
    }

    public int Priority
    {
        get => _rule.Priority;
        set { if (_rule.Priority != value) { _rule.Priority = value; OnPropertyChanged(); } }
    }

    public string TargetFeatureClass
    {
        get => _rule.TargetFeatureClass;
        set { if (_rule.TargetFeatureClass != value) { _rule.TargetFeatureClass = value; OnPropertyChanged(); } }
    }

    public string LayerNamesCsv
    {
        get => _rule.LayerNames != null ? string.Join(", ", _rule.LayerNames) : string.Empty;
        set 
        {
            _rule.LayerNames = string.IsNullOrWhiteSpace(value) ? null : value.Split(',').Select(s => s.Trim()).ToArray();
            OnPropertyChanged();
        }
    }

    public string EntityTypesCsv
    {
        get => _rule.EntityTypes != null ? string.Join(", ", _rule.EntityTypes) : string.Empty;
        set 
        {
            _rule.EntityTypes = string.IsNullOrWhiteSpace(value) ? null : value.Split(',').Select(s => s.Trim()).ToArray();
            OnPropertyChanged();
        }
    }

    public string ColorsCsv
    {
        get => _rule.Colors != null ? string.Join(", ", _rule.Colors) : string.Empty;
        set 
        {
            _rule.Colors = string.IsNullOrWhiteSpace(value) ? null : value.Split(',').Select(s => s.Trim()).ToArray();
            OnPropertyChanged();
        }
    }

    public string GeometryTypesCsv
    {
        get => _rule.GeometryTypes != null ? string.Join(", ", _rule.GeometryTypes) : string.Empty;
        set 
        {
            _rule.GeometryTypes = string.IsNullOrWhiteSpace(value) ? null : value.Split(',').Select(s => s.Trim()).ToArray();
            OnPropertyChanged();
        }
    }

    public double? MinimumArea
    {
        get => _rule.MinimumArea;
        set { if (_rule.MinimumArea != value) { _rule.MinimumArea = value; OnPropertyChanged(); } }
    }

    public double? MaximumArea
    {
        get => _rule.MaximumArea;
        set { if (_rule.MaximumArea != value) { _rule.MaximumArea = value; OnPropertyChanged(); } }
    }

    public string? TextPattern
    {
        get => _rule.TextPattern;
        set { if (_rule.TextPattern != value) { _rule.TextPattern = value; OnPropertyChanged(); } }
    }

    public string? RequiredCRS
    {
        get => _rule.RequiredCRS;
        set { if (_rule.RequiredCRS != value) { _rule.RequiredCRS = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
