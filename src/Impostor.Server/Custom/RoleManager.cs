using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Impostor.Api.Events.Managers;
using Impostor.Api.Games;
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
        await Task.Delay(50);

        foreach (var player in game.Players.Cast<InnerPlayerControl>())
        {
            if (player != null && game.Host != null)
            {
                using var writer = game.StartGameData();
                writer.StartMessage(1);
                writer.WritePacked(player.PlayerInfo.NetId);
                await player.PlayerInfo.SerializeAsync(writer, false);
                writer.EndMessage();
                writer.EndMessage();
                await game.SendToAllExceptAsync(writer, game.Host.Client.Id);
            }
        }
    }

    public static async ValueTask ResyncRoles(Game game)
    {
        foreach (var player in game.Players.Cast<InnerPlayerControl>())
        {
            if (player?.PlayerInfo?.RoleType == null)
            {
                return;
            }

            await player.SetRoleAsync(game, (RoleTypes)player.PlayerInfo.RoleType);
        }
    }

    public static async ValueTask FixBlackScreen(Game game)
    {
        await Task.Delay(100);

        foreach (InnerPlayerControl player in game.Players.Where(pc => !pc.IsHost && (pc.Character != null && !pc.Character.PlayerInfo.IsImpostor)))
        {
            var setPlayer = game.Players.FirstOrDefault(pc => pc != player && pc.Character != null && !pc.Character.PlayerInfo.IsDead && pc.Character.PlayerInfo.LastDeathReason != DeathReason.Exile) as InnerPlayerControl;

            if (setPlayer != null)
            {
                _ = setPlayer.SetRoleForAsync(game, RoleTypes.Impostor, player);

                await Task.Delay(1000);

                _ = setPlayer.SetRoleForAsync(game, RoleTypes.Crewmate, player);
            }
        }
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

        await game.FinishGameDataAsync(writer);

        if (isIntro)
        {
            player.PlayerInfo.Disconnected = false;
        }
    }

    public static async ValueTask SetRoleForAsync(this InnerPlayerControl player, Game game, RoleTypes role, IInnerPlayerControl? target = null, bool isIntro = false)
    {
        if (target == null)
        {
            target = player;
        }

        using var writer = game.StartGameData(game.Players.Select(p => p.Client).First(p => p.Player?.Character == target).Id);

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

            var newRole = pc.PlayerInfo.IsImpostor ? player.PlayerInfo.IsDead ? RoleTypes.ImpostorGhost : RoleTypes.Impostor : role;

            using var writer = game.StartGameData(game.Players.Select(p => p.Client).First(p => p.Player?.Character == pc).Id);

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

            Rpc44SetRole.Serialize(writer, newRole, true);
            writer.EndMessage();

            await game.FinishGameDataAsync(writer, game.Players.Select(p => p.Client).First(p => p.Player?.Character == pc).Id);

            if (isIntro)
            {
                player.PlayerInfo.Disconnected = false;
            }
        }
    }
}
