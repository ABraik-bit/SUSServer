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

    public static async ValueTask FixBlackScreen(Game game)
    {
        // _logger.LogInformation("Black screen prevention has started!");

        await Task.Delay(4000);

        Dictionary<InnerPlayerControl, InnerPlayerControl> setPlayers = [];

        InnerPlayerControl? SyncPlayer(InnerPlayerControl target) =>
            game.ClientPlayers
                .Where(p => !p.IsHost && p.Character != target && !p.Character.PlayerInfo.IsDead)
                .FirstOrDefault()?.Character;


        foreach (var player in game.ClientPlayers.Where(p => !p.IsHost
        && !p.Character.PlayerInfo.IsImpostor
        && !p.Character.PlayerInfo.IsDead).Select(p => p.Character))
        {
            var sycnPlayer = SyncPlayer(player);
            await sycnPlayer.SetRoleForAsync(game, RoleTypes.Impostor, player);
            setPlayers[player] = sycnPlayer;
        }

        await Task.Delay(12000);

        foreach (var kvp in setPlayers)
        {
            if (kvp.Key != null && kvp.Value != null && game != null)
            {
                _ = kvp.Value.SetRoleForAsync(game, kvp.Value.PlayerInfo.IsDead ? RoleTypes.CrewmateGhost : RoleTypes.Crewmate, kvp.Key);
            }
        }

        // _logger.LogInformation("Black screen prevention has ended.");
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
        if (game.Host.Character == player)
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
