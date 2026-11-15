using Gsa.Core.Geometry;
using Gsa.Missions.Campaign;
using Gsa.Missions.Content;
using Gsa.Missions.Contracts;
using Gsa.Missions.Presentation;
using Godot;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine;

/// <summary>Engine-safe values for GDScript; domain objects remain private.</summary>
public partial class GameFacade : Node
{
    private readonly GodotCoordinateAdapter _coordinates = new();

    public Dictionary GetStatus() => new()
    {
        ["schema_version"] = 1,
        ["datum_id"] = LocalMapFrame.DatumId,
        ["physics_ticks_per_second"] = 60,
        ["missions_module"] = typeof(ContractValidator).Assembly.GetName().Name ?? "",
    };

    public Dictionary GetHudCapabilities(
        int platformLevel,
        bool factionsUnlocked = false,
        bool hasGimbalina = true,
        bool hasCamera = true,
        bool hasGnss = true)
    {
        var context = new HudVisibilityContext(platformLevel, factionsUnlocked, hasGimbalina, hasCamera, hasGnss);
        return new Dictionary
        {
            ["platform_level"] = context.PlatformLevel,
            ["show_lidar_panel"] = context.PlatformLevel >= 4,
            ["show_faction_attention"] = context.FactionsUnlocked,
            ["show_gimbalina_stress"] = context.HasGimbalina,
            ["has_camera"] = context.HasCamera,
            ["has_gnss"] = context.HasGnss,
        };
    }

    public Dictionary GetContractBoardCardSummary(string contractJson, string locale = "en-US")
    {
        try
        {
            var definition = ContractDefinition.Parse(contractJson, "facade_contract.json");
            var card = BoardOfficeAndReportPresenter.BuildContractBoardCard(definition, locale);
            return new Dictionary
            {
                ["status"] = "accepted",
                ["contract_id"] = card.ContractId,
                ["title_key"] = card.TitleKey,
                ["client_id"] = card.ClientId,
                ["region_id"] = card.RegionId,
                ["area_id"] = card.AreaId,
                ["product"] = card.Product,
                ["is_calm"] = card.IsCalm,
                ["calm_badge_key"] = card.CalmBadgeKey,
                ["due_hours"] = card.FormattedDueHours,
                ["expires_hours"] = card.FormattedExpiresHours,
                ["base_coords"] = card.BaseCoords,
                ["base_xp"] = card.BaseXp,
                ["late_penalty_per_hour"] = card.FormattedLatePenaltyPerHour,
                ["late_penalty_cap"] = card.FormattedLatePenaltyCap,
                ["partial_policy_mode"] = card.PartialPolicyMode,
                ["requirement_count"] = card.Requirements.Count,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return InvalidInput(exception.Message);
        }
    }

    public Dictionary MapToLocal(double east, double north, double up)
    {
        try
        {
            return new Dictionary
            {
                ["status"] = "accepted",
                ["position"] = _coordinates.ToGodot(new Vector3d(east, north, up)),
            };
        }
        catch (ArgumentException exception)
        {
            return InvalidInput(exception.Message);
        }
    }

    public Dictionary LocalToMap(Vector3 position)
    {
        try
        {
            Vector3d enu = _coordinates.ToMap(position);
            return new Dictionary
            {
                ["status"] = "accepted",
                ["east"] = enu.X,
                ["north"] = enu.Y,
                ["up"] = enu.Z,
            };
        }
        catch (ArgumentException exception)
        {
            return InvalidInput(exception.Message);
        }
    }

    public Dictionary RunM2TutorialSmoke(string contentDirectory, string worldManifestPath)
    {
        if (string.IsNullOrWhiteSpace(contentDirectory) || string.IsNullOrWhiteSpace(worldManifestPath))
        {
            return InvalidInput("Content directory and world manifest path are required.");
        }

        var report = TutorialMissionRunner.RunHist01Playthrough(contentDirectory, worldManifestPath);
        return new Dictionary
        {
            ["status"] = report.FinalReportPassed ? "ok" : "failed",
            ["mission_id"] = report.MissionId,
            ["contract_id"] = report.ContractId,
            ["stars"] = report.FinalStars,
            ["final_coords"] = report.FinalLedgerCoords,
            ["unlocked_missions"] = string.Join(",", report.UnlockedMissions),
            ["unlocked_vehicles"] = string.Join(",", report.UnlockedVehicles),
        };
    }

    private static Dictionary InvalidInput(string reason) => new()
    {
        ["status"] = "invalid_input",
        ["reason"] = reason,
    };
}
