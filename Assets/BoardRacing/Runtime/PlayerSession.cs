using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Board.Core;
using Board.Session;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    internal interface IPlayerSession : IDisposable
    {
        event Action PlayersChanged;
        IReadOnlyList<SessionPlayer> Players { get; }
        bool SelectorInFlight { get; }
        Texture2D AvatarFor(int sessionId);
        Task AddPlayer();
        Task EditPlayer(int sessionId);
        void ShowProfileSwitcher();
        void HideProfileSwitcher();
    }

    internal sealed class BoardPlayerSession : IPlayerSession
    {
        public event Action PlayersChanged;
        public bool SelectorInFlight { get; private set; }

        public BoardPlayerSession() => BoardSession.playersChanged += OnPlayersChanged;

        public IReadOnlyList<SessionPlayer> Players => BoardSession.players
            .Take(4)
            .Select(x => new SessionPlayer(x.sessionId, x.playerId, x.name, x.avatarId))
            .ToArray();

        public Texture2D AvatarFor(int sessionId) =>
            BoardSession.players.FirstOrDefault(x => x.sessionId == sessionId)?.avatar;

        public async Task AddPlayer()
        {
            if (SelectorInFlight || Players.Count >= 4) return;
            await Present(() => BoardSession.PresentAddPlayerSelector());
        }

        public async Task EditPlayer(int sessionId)
        {
            if (SelectorInFlight) return;
            BoardSessionPlayer player = BoardSession.players
                .FirstOrDefault(x => x.sessionId == sessionId);
            if (player != null) await Present(() => BoardSession.PresentReplacePlayerSelector(player));
        }

        public void ShowProfileSwitcher() => BoardApplication.ShowProfileSwitcher();
        public void HideProfileSwitcher() => BoardApplication.HideProfileSwitcher();

        public void Dispose() => BoardSession.playersChanged -= OnPlayersChanged;

        private async Task Present(Func<Task<bool>> selector)
        {
            SelectorInFlight = true;
            try
            {
                await selector();
            }
            catch (InvalidOperationException)
            {
                // Old BoardOS/editor builds can lack native selector support.
                // The lobby remains usable and keeps its current roster.
            }
            finally
            {
                SelectorInFlight = false;
            }
        }

        private void OnPlayersChanged() => PlayersChanged?.Invoke();
    }

    internal sealed class FallbackPlayerSession : IPlayerSession
    {
        private readonly List<SessionPlayer> players = new List<SessionPlayer>();
        private int nextId = 1;

        public FallbackPlayerSession(int initialPlayers = 2)
        {
            for (int i = 0; i < initialPlayers; i++) AddPlayerNow();
        }

        public event Action PlayersChanged;
        public IReadOnlyList<SessionPlayer> Players => players.ToArray();
        public bool SelectorInFlight => false;
        public Texture2D AvatarFor(int sessionId) => null;

        public Task AddPlayer()
        {
            if (players.Count < 4)
            {
                AddPlayerNow();
                PlayersChanged?.Invoke();
            }
            return Task.CompletedTask;
        }

        public Task EditPlayer(int sessionId)
        {
            int index = players.FindIndex(x => x.SessionId == sessionId);
            if (index >= 0)
            {
                SessionPlayer player = players[index];
                players[index] = new SessionPlayer(player.SessionId, player.PlayerId,
                    player.DisplayName.EndsWith("*", StringComparison.Ordinal)
                        ? player.DisplayName.TrimEnd('*') : player.DisplayName + "*",
                    player.AvatarId);
                PlayersChanged?.Invoke();
            }
            return Task.CompletedTask;
        }

        public void ShowProfileSwitcher() { }
        public void HideProfileSwitcher() { }
        public void Dispose() { }

        private void AddPlayerNow()
        {
            int id = nextId++;
            players.Add(new SessionPlayer(id, "fallback-" + id,
                "Player " + id, "fallback-avatar-" + id));
        }
    }
}
