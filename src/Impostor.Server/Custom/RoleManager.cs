using System.Data;
using System.Numerics;
using Impostor.Api.Innersloth;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Inner.Objects;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Server.Net.Inner;
using Impostor.Server.Net.Inner.Objects;
using Impostor.Server.Net.State;

namespace Impostor.Server.Custom;

internal static class RoleManager
{
    public static async ValueTask MigrateHostExtension(Game game)
    {
        var host = game.Host;

        if (host?.Character == null)
        {
            return;
        }

        foreach (var player in game.ClientPlayers)
        {
            if (player?.Character?.PlayerInfo?.RoleType == null)
            {
                continue;
            }

            await player.Character.SetRoleForAsync(game, (RoleTypes)player.Character.PlayerInfo.RoleType, host.Character);
        }
    }

    public static async ValueTask SyncData(Game game)
    {
        foreach (var player in game.ClientPlayers)
        {
            if (player?.Character != null && game.Host != null)
            {
                using var writer = game.StartGameData();
                writer.StartMessage(1);
                writer.WritePacked(player.Character.PlayerInfo.NetId);
                await player.Character.PlayerInfo.SerializeAsync(writer, false);
                writer.EndMessage();
                writer.EndMessage();
                await game.SendToAllExceptAsync(writer, game.Host.Client.Id);
            }
        }
    }

    public static async ValueTask ResyncRoles(Game game)
    {
        foreach (var player in game.ClientPlayers)
        {
            if (player?.Character?.PlayerInfo?.RoleType == null)
            {
                return;
            }

            await player.Character.SetRoleAsync(game, (RoleTypes)player.Character.PlayerInfo.RoleType);
        }
    }

    public static async ValueTask FixBlackScreen(Game game, InnerPlayerControl? exiled)
    {
        // _logger.LogInformation("Black screen prevention has started!");

        var alivePlayers = game.ClientPlayers.Where(pc => pc?.Character?.PlayerInfo != null && !pc.Character.PlayerInfo.IsDead && !pc.Character.PlayerInfo.Disconnected);

        if (alivePlayers.Where(pc => pc.Character != null && !pc.Character.PlayerInfo.IsImpostor).Count() <=
            alivePlayers.Where(pc => pc.Character != null && pc.Character.PlayerInfo.IsImpostor).Count())
        {
            return;
        }

        await Task.Delay(8500);

        Dictionary<InnerPlayerControl, InnerPlayerControl> setPlayers = [];

        foreach (var player in game.ClientPlayers.Where(p => !p.IsHost
        && p.Character != null && !p.Character.PlayerInfo.IsImpostor).Select(p => p.Character))
        {
            if (player == null)
            {
                continue;
            }

            var sycnPlayer = game.ClientPlayers.FirstOrDefault(p =>
            p.Character != null &&
            !p.Character.PlayerInfo.IsDead &&
            !p.Character.PlayerInfo.Disconnected &&
            p.Character != player &&
            p.Character != exiled)
            ?.Character;

            if (sycnPlayer == null)
            {
                continue;
            }

            _ = sycnPlayer.SetRoleForAsync(game, RoleTypes.Impostor, player);
            setPlayers[player] = sycnPlayer;
        }

        await Task.Delay(4500);

        foreach (var kvp in setPlayers)
        {
            if (kvp.Key != null && kvp.Value != null && game != null)
            {
                _ = kvp.Value.SetRoleForAsync(game, kvp.Value.PlayerInfo.IsDead ? RoleTypes.CrewmateGhost : RoleTypes.Crewmate, kvp.Key);
            }
        }

        // _logger.LogInformation("Black screen prevention has ended.");
    }

    public static async ValueTask AssignRoles(Game game)
    {
        foreach (var player in game.ClientPlayers)
        {
            if (player?.Character?.PlayerInfo?.RoleType == null)
            {
                continue;
            }

            _ = player.Character.SetRoleForAsync(game, (RoleTypes)player.Character.PlayerInfo.RoleType, player.Character, true);
            _ = player.Character.SetRoleForDesync(game, RoleTypes.Crewmate, [player.Character, game.Host?.Character], true);
        }

        await Task.Delay(200);
    }

    public static async ValueTask SetRoleAsync(this InnerPlayerControl player, Game game, RoleTypes role, bool isIntro = false)
    {
        using var writer = game.StartGameData();

        if (isIntro)
        {
            player.PlayerInfo.Disconnected = true;
            writer.StartMessage(1);
            writer.WritePacked(player.PlayerInfo.NetId);
            await player.PlayerInfo.SerializeAsync(writer, false);
            writer.EndMessage();
        }

        writer.StartMessage(GameDataTag.RpcFlag);
        writer.WritePacked(player.NetId);
        writer.Write((byte)RpcCalls.SetRole);

        Rpc44SetRole.Serialize(writer, role, true);
        writer.EndMessage();
        writer.EndMessage();

        await game.SendToAllExceptAsync(writer, game.Host.Client.Id);

        if (isIntro)
        {
            player.PlayerInfo.Disconnected = false;
        }

        // _logger.LogInformation($"Set {player.PlayerInfo.PlayerName} Role to {role} for all players");
    }

    public static async ValueTask SetRoleForAsync(this InnerPlayerControl player, Game game, RoleTypes role, IInnerPlayerControl? target = null, bool isIntro = false)
    {
        if (game.Host.Character == target)
        {
            return;
        }

        if (target == null)
        {
            target = player;
        }

        using var writer = game.StartGameData();

        if (isIntro)
        {
            player.PlayerInfo.Disconnected = true;
            writer.StartMessage(1);
            writer.WritePacked(player.PlayerInfo.NetId);
            await player.PlayerInfo.SerializeAsync(writer, false);
            writer.EndMessage();
        }

        writer.StartMessage(GameDataTag.RpcFlag);
        writer.WritePacked(player.NetId);
        writer.Write((byte)RpcCalls.SetRole);

        Rpc44SetRole.Serialize(writer, role, true);
        writer.EndMessage();

        await game.FinishGameDataAsync(writer, game.Players.Select(p => p.Client).First(p => p.Player?.Character == target).Id);

        if (isIntro)
        {
            player.PlayerInfo.Disconnected = false;
        }

        // _logger.LogInformation($"Set {player.PlayerInfo.PlayerName} Role to {role} for {target.PlayerInfo.PlayerName}");
    }

    public static async ValueTask SetRoleForDesync(this InnerPlayerControl player, Game game, RoleTypes role, IInnerPlayerControl?[] targets, bool isIntro = false)
    {
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
            {
                targets[i] = player;
            }
        }

        foreach (var pc in game.Players.Select(p => p.Character))
        {
            if (pc == null || targets.Contains(pc))
            {
                continue;
            }

            using var writer = game.StartGameData();

            if (isIntro)
            {
                player.PlayerInfo.Disconnected = true;
                writer.StartMessage(1);
                writer.WritePacked(player.PlayerInfo.NetId);
                await player.PlayerInfo.SerializeAsync(writer, false);
                writer.EndMessage();
            }

            writer.StartMessage(GameDataTag.RpcFlag);
            writer.WritePacked(player.NetId);
            writer.Write((byte)RpcCalls.SetRole);

            var newRole = pc.PlayerInfo.IsImpostor && player.PlayerInfo.IsImpostor
                ? (player.PlayerInfo.IsDead ? RoleTypes.ImpostorGhost : RoleTypes.Impostor)
                : role;
            Rpc44SetRole.Serialize(writer, newRole, true);
            writer.EndMessage();

            await game.FinishGameDataAsync(writer, game.Players.Select(p => p.Client).First(p => p.Player?.Character == pc).Id);

            if (isIntro)
            {
                player.PlayerInfo.Disconnected = false;
            }

            // _logger.LogInformation($"Desync {player.PlayerInfo.PlayerName} Role to {role} for {pc.PlayerInfo.PlayerName}");
        }
    }
}
