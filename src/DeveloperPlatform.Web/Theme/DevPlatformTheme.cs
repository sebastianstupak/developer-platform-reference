using MudBlazor;

namespace DeveloperPlatform.Web.Theme;

public static class DevPlatformTheme
{
    private static readonly string[] _inter =
    [
        "Inter", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif",
    ];

    public static readonly MudTheme Instance = new()
    {
        PaletteLight = new PaletteLight
        {
            Black = "#09090b",
            White = "#ffffff",
            Primary = "#18181b",
            PrimaryContrastText = "#fafafa",
            Secondary = "#71717a",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#3f3f46",
            TertiaryContrastText = "#fafafa",
            Info = "#2563eb",
            InfoContrastText = "#ffffff",
            Success = "#16a34a",
            SuccessContrastText = "#ffffff",
            Warning = "#d97706",
            WarningContrastText = "#ffffff",
            Error = "#dc2626",
            ErrorContrastText = "#ffffff",
            Dark = "#27272a",
            DarkContrastText = "#fafafa",
            Background = "#ffffff",
            BackgroundGray = "#f4f4f5",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#18181b",
            DrawerBackground = "#fafafa",
            DrawerText = "#18181b",
            DrawerIcon = "#71717a",
            TextPrimary = "#18181b",
            TextSecondary = "#71717a",
            TextDisabled = "rgba(24,24,27,0.38)",
            ActionDefault = "#71717a",
            ActionDisabled = "rgba(24,24,27,0.26)",
            ActionDisabledBackground = "rgba(24,24,27,0.12)",
            LinesDefault = "#e4e4e7",
            LinesInputs = "#d4d4d8",
            TableLines = "#e4e4e7",
            TableStriped = "rgba(24,24,27,0.02)",
            TableHover = "rgba(24,24,27,0.04)",
            Divider = "#e4e4e7",
            DividerLight = "rgba(24,24,27,0.06)",
            Skeleton = "rgba(24,24,27,0.11)",
            OverlayLight = "rgba(255,255,255,0.5)",
            OverlayDark = "rgba(9,9,11,0.5)",
        },
        PaletteDark = new PaletteDark
        {
            Black = "#000000",
            White = "#ffffff",
            Primary = "#fafafa",
            PrimaryContrastText = "#18181b",
            Secondary = "#a1a1aa",
            SecondaryContrastText = "#09090b",
            Tertiary = "#d4d4d8",
            TertiaryContrastText = "#09090b",
            Info = "#60a5fa",
            InfoContrastText = "#09090b",
            Success = "#4ade80",
            SuccessContrastText = "#09090b",
            Warning = "#fbbf24",
            WarningContrastText = "#09090b",
            Error = "#f87171",
            ErrorContrastText = "#09090b",
            Dark = "#09090b",
            DarkContrastText = "#fafafa",
            Background = "#09090b",
            BackgroundGray = "#27272a",
            Surface = "#18181b",
            AppbarBackground = "#09090b",
            AppbarText = "rgba(250,250,250,0.87)",
            DrawerBackground = "#18181b",
            DrawerText = "rgba(250,250,250,0.87)",
            DrawerIcon = "#a1a1aa",
            TextPrimary = "rgba(250,250,250,0.87)",
            TextSecondary = "rgba(161,161,170,0.7)",
            TextDisabled = "rgba(250,250,250,0.2)",
            ActionDefault = "#a1a1aa",
            ActionDisabled = "rgba(250,250,250,0.26)",
            ActionDisabledBackground = "rgba(250,250,250,0.12)",
            LinesDefault = "#3f3f46",
            LinesInputs = "rgba(250,250,250,0.3)",
            TableLines = "#3f3f46",
            TableStriped = "rgba(250,250,250,0.02)",
            TableHover = "rgba(250,250,250,0.04)",
            Divider = "#27272a",
            DividerLight = "rgba(250,250,250,0.06)",
            Skeleton = "rgba(250,250,250,0.11)",
            OverlayLight = "rgba(255,255,255,0.1)",
            OverlayDark = "rgba(0,0,0,0.7)",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = _inter,
                FontSize = ".875rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = "0em",
            },
            H1 = new H1Typography
            {
                FontFamily = _inter,
                FontSize = "2.25rem",
                FontWeight = "700",
                LineHeight = "1.2",
                LetterSpacing = "-0.025em",
            },
            H2 = new H2Typography
            {
                FontFamily = _inter,
                FontSize = "1.875rem",
                FontWeight = "700",
                LineHeight = "1.25",
                LetterSpacing = "-0.02em",
            },
            H3 = new H3Typography
            {
                FontFamily = _inter,
                FontSize = "1.5rem",
                FontWeight = "600",
                LineHeight = "1.3",
                LetterSpacing = "-0.015em",
            },
            H4 = new H4Typography
            {
                FontFamily = _inter,
                FontSize = "1.25rem",
                FontWeight = "600",
                LineHeight = "1.35",
                LetterSpacing = "-0.01em",
            },
            H5 = new H5Typography
            {
                FontFamily = _inter,
                FontSize = "1.125rem",
                FontWeight = "600",
                LineHeight = "1.4",
                LetterSpacing = "0em",
            },
            H6 = new H6Typography
            {
                FontFamily = _inter,
                FontSize = "1rem",
                FontWeight = "600",
                LineHeight = "1.4",
                LetterSpacing = "0em",
            },
            Subtitle1 = new Subtitle1Typography
            {
                FontFamily = _inter,
                FontSize = ".875rem",
                FontWeight = "500",
                LineHeight = "1.5",
                LetterSpacing = "0em",
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontFamily = _inter,
                FontSize = ".75rem",
                FontWeight = "500",
                LineHeight = "1.5",
                LetterSpacing = "0em",
            },
            Body1 = new Body1Typography
            {
                FontFamily = _inter,
                FontSize = ".875rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = "0em",
            },
            Body2 = new Body2Typography
            {
                FontFamily = _inter,
                FontSize = ".8125rem",
                FontWeight = "400",
                LineHeight = "1.43",
                LetterSpacing = "0em",
            },
            Button = new ButtonTypography
            {
                FontFamily = _inter,
                FontSize = ".875rem",
                FontWeight = "500",
                LineHeight = "1.5",
                LetterSpacing = "0em",
                TextTransform = "none",
            },
            Caption = new CaptionTypography
            {
                FontFamily = _inter,
                FontSize = ".75rem",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = "0em",
            },
            Overline = new OverlineTypography
            {
                FontFamily = _inter,
                FontSize = ".625rem",
                FontWeight = "400",
                LineHeight = "2.66",
                LetterSpacing = ".08333em",
                TextTransform = "uppercase",
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "0.5rem",
            AppbarHeight = "56px",
            DrawerWidthLeft = "260px",
            DrawerWidthRight = "300px",
        },
    };
}
