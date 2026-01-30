namespace EliteWhisper.Models
{
    /// <summary>
    /// Visual state of the floating widget (separate from dictation state).
    /// </summary>
    public enum WidgetVisualState
    {
        /// <summary>Default compact pill showing status only.</summary>
        Collapsed,
        
        /// <summary>Pill expanded on hover to show action buttons.</summary>
        HoverExpanded,
        
        /// <summary>Full widget panel with waveform and controls.</summary>
        Expanded
    }
}
