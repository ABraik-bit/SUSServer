using System.Numerics;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.Customization;
using Impostor.Api.Net.Inner;
using Impostor.Api.Net.Inner.Objects;
using Impostor.Api.Net.Inner.Objects.Components;
using Impostor.Api.Net.Messages.Rpcs;
using Impostor.Server.Events.Player;
using Impostor.Server.Net.Manager;

namespace Impostor.Server.Net.Inner.Objects
{
    internal partial class InnerPlayerControl : IInnerPlayerControl
    {
        IInnerPlayerPhysics IInnerPlayerControl.Physics => Physics;

        IInnerCustomNetworkTransform IInnerPlayerControl.NetworkTransform => NetworkTransform;

        IInnerPlayerInfo IInnerPlayerControl.PlayerInfo => PlayerInfo;

        public async ValueTask SetNameAsync(string name)
        {
            PlayerInfo.CurrentOutfit.PlayerName = name;

            using var writer = Game.StartRpc(NetId, RpcCalls.SetName);
            writer.Write(name);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask SetColorAsync(ColorType color)
        {
            PlayerInfo.CurrentOutfit.Color = color;

            using var writer = Game.StartRpc(NetId, RpcCalls.SetColor);
            Rpc08SetColor.Serialize(writer, PlayerInfo.NetId, color);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask SetHatAsync(string hatId)
        {
            PlayerInfo.CurrentOutfit.HatId = hatId;

            using var writer = Game.StartRpc(NetId, RpcCalls.SetHat);
            Rpc39SetHatStr.Serialize(writer, hatId);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask SetPetAsync(string petId)
        {
            PlayerInfo.CurrentOutfit.PetId = petId;

            using var writer = Game.StartRpc(NetId, RpcCalls.SetPet);
            Rpc41SetPetStr.Serialize(writer, petId);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask SetSkinAsync(string skinId)
        {
            PlayerInfo.CurrentOutfit.SkinId = skinId;

            using var writer = Game.StartRpc(NetId, RpcCalls.SetSkin);
            Rpc40SetSkinStr.Serialize(writer, skinId);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask SendChatAsync(string text)
        {
            using var writer = Game.StartRpc(NetId, RpcCalls.SendChat);
            writer.Write(text);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask SendChatToPlayerAsync(string text, IInnerPlayerControl? player = null)
        {
            if (player == null)
            {
                player = this;
            }

            using var writer = Game.StartRpc(NetId, RpcCalls.SendChat);
            writer.Write(text);
            await Game.FinishRpcAsync(writer, player.OwnerId);
        }

        public async ValueTask SetRoleAsync(RoleTypes role, bool isIntro = false)
        {
            using var writer = Game.StartGameData();

            if (isIntro)
            {
                PlayerInfo.Disconnected = true;
                writer.StartMessage(1);
                writer.WritePacked(PlayerInfo.NetId);
                await PlayerInfo.SerializeAsync(writer, false);
                writer.EndMessage();
            }

            writer.StartMessage(GameDataTag.RpcFlag);
            writer.WritePacked(NetId);
            writer.Write((byte)RpcCalls.SetRole);

            Rpc44SetRole.Serialize(writer, role, true);
            writer.EndMessage();

            await Game.FinishGameDataAsync(writer);

            if (isIntro)
            {
                PlayerInfo.Disconnected = false;
            }
        }

        public async ValueTask SetRoleForAsync(RoleTypes role, IInnerPlayerControl? player = null, bool isIntro = false)
        {
            if (player == null)
            {
                player = this;
            }

            using var writer = Game.StartGameData(Game.Players.Select(p => p.Client).First(p => p.Player?.Character == player).Id);

            if (isIntro)
            {
                PlayerInfo.Disconnected = true;
                writer.StartMessage(1);
                writer.WritePacked(PlayerInfo.NetId);
                await PlayerInfo.SerializeAsync(writer, false);
                writer.EndMessage();
            }

            writer.StartMessage(GameDataTag.RpcFlag);
            writer.WritePacked(NetId);
            writer.Write((byte)RpcCalls.SetRole);

            Rpc44SetRole.Serialize(writer, role, true);
            writer.EndMessage();

            await Game.FinishGameDataAsync(writer, Game.Players.Select(p => p.Client).First(p => p.Player?.Character == player).Id);

            if (isIntro)
            {
                PlayerInfo.Disconnected = false;
            }
        }

        public async ValueTask SetRoleForDesync(RoleTypes role, IInnerPlayerControl?[] players, bool isIntro = false)
        {
            for (var i = 0; i < players.Length; i++)
            {
                if (players[i] == null)
                {
                    players[i] = this;
                }
            }

            foreach (var pc in Game.Players.Select(p => p.Character))
            {
                if (pc == null || players.Contains(pc))
                {
                    continue;
                }

                using var writer = Game.StartGameData(Game.Players.Select(p => p.Client).First(p => p.Player?.Character == pc).Id);

                if (isIntro)
                {
                    PlayerInfo.Disconnected = true;
                    writer.StartMessage(1);
                    writer.WritePacked(PlayerInfo.NetId);
                    await PlayerInfo.SerializeAsync(writer, false);
                    writer.EndMessage();
                }

                writer.StartMessage(GameDataTag.RpcFlag);
                writer.WritePacked(NetId);
                writer.Write((byte)RpcCalls.SetRole);

                Rpc44SetRole.Serialize(writer, role, true);
                writer.EndMessage();

                await Game.FinishGameDataAsync(writer, Game.Players.Select(p => p.Client).First(p => p.Player?.Character == pc).Id);

                if (isIntro)
                {
                    PlayerInfo.Disconnected = false;
                }
            }
        }

        public async ValueTask MurderPlayerAsync(IInnerPlayerControl target, MurderResultFlags result)
        {
            if (!PlayerInfo.IsImpostor)
            {
                throw new ImpostorProtocolException("Tried to murder a player, but murderer was not the impostor.");
            }

            if (PlayerInfo.IsDead)
            {
                throw new ImpostorProtocolException("Tried to murder a player, but murderer was not alive.");
            }

            if (target.PlayerInfo.IsDead)
            {
                throw new ImpostorProtocolException("Tried to murder a player, but target was not alive.");
            }

            if (!result.IsFailed())
            {
                ((InnerPlayerControl)target).Die(DeathReason.Kill);
            }

            using var writer = Game.StartRpc(NetId, RpcCalls.MurderPlayer);
            Rpc12MurderPlayer.Serialize(writer, target, result);
            await Game.FinishRpcAsync(writer);

            await _eventManager.CallAsync(new PlayerMurderEvent(Game, Game.GetClientPlayer(OwnerId)!, this, target, result));
        }

        public async ValueTask MurderPlayerAsync(IInnerPlayerControl target)
        {
            await MurderPlayerAsync(target, MurderResultFlags.Succeeded);
        }

        public async ValueTask ProtectPlayerAsync(IInnerPlayerControl target)
        {
            if (target.PlayerInfo.RoleType == RoleTypes.GuardianAngel)
            {
                throw new ImpostorProtocolException("Tried to protect another Guardian Angel");
            }

            ((InnerPlayerControl)target).Protect(this);

            using var writer = Game.StartRpc(NetId, RpcCalls.ProtectPlayer);
            Rpc45ProtectPlayer.Serialize(writer, target, PlayerInfo.CurrentOutfit.Color);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask ExileAsync()
        {
            if (PlayerInfo.IsDead)
            {
                throw new ImpostorProtocolException("Tried to exile a player, but target was not alive.");
            }

            // Update player.
            Die(DeathReason.Exile);

            // Send RPC.
            using var writer = Game.StartRpc(NetId, RpcCalls.Exiled);
            Rpc04Exiled.Serialize(writer);
            await Game.FinishRpcAsync(writer);

            // Notify plugins.
            await _eventManager.CallAsync(new PlayerExileEvent(Game, Game.GetClientPlayer(OwnerId)!, this));
        }

        public async ValueTask StartVanishAsync()
        {
            using var writer = Game.StartRpc(NetId, RpcCalls.StartVanish);
            Rpc63StartVanish.Serialize(writer);
            await Game.FinishRpcAsync(writer);
        }

        public async ValueTask StartAppearAsync(bool shouldAnimate)
        {
            using var writer = Game.StartRpc(NetId, RpcCalls.StartAppear);
            Rpc65StartAppear.Serialize(writer, shouldAnimate);
            await Game.FinishRpcAsync(writer);
        }
    }
}
