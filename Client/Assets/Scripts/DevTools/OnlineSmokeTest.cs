using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Loot;
using Game.Shared.Net;
using Game.Shared.Social;
using Game.Shared.Progression;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Verificação automática do modo online. Rodar com
    /// <c>-connect host:porta -name Nome -onlinetest pasta</c>: conecta, espera ver as entidades do servidor,
    /// tira screenshot, grava um relatório (SUCESSO/FALHA) e fecha. <c>-onlinefight</c> também luta contra um coelho
    /// pelo servidor. <c>-onlinewait N</c> mantém o cliente aberto N segundos (para outro cliente de teste enxergá-lo).
    /// </summary>
    public sealed class OnlineSmokeTest : MonoBehaviour
    {
        private string _folder;
        private float _stayOnline;
        private bool _fight;
        private bool _uiTour;
        private float _perfSeconds;
        private bool _social;
        private bool _dungeon;
        private string _dungeonId = Shared.Dungeon.DungeonCatalog.CryptId;
        private bool _arena;
        private bool _switch;
        private StreamWriter _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-onlinetest");
            // Com -autologin o jogo só fica online depois do menu recarregar a cena: o teste sobrevive à troca e espera.
            if (index < 0 || (!GameMode.IsOnline && string.IsNullOrEmpty(GameMode.AutoLoginAccount))) return;

            var test = new GameObject("OnlineSmokeTest").AddComponent<OnlineSmokeTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "onlinetest");
            var wait = Array.IndexOf(args, "-onlinewait");
            if (wait >= 0 && wait + 1 < args.Length) float.TryParse(args[wait + 1], out test._stayOnline);
            test._fight = Array.IndexOf(args, "-onlinefight") >= 0;
            test._uiTour = Array.IndexOf(args, "-onlineui") >= 0;
            test._social = Array.IndexOf(args, "-onlinesocial") >= 0;
            test._dungeon = Array.IndexOf(args, "-onlinedungeon") >= 0 || Array.IndexOf(args, "-onlinesanctuary") >= 0;
            if (Array.IndexOf(args, "-onlinesanctuary") >= 0) test._dungeonId = Shared.Dungeon.DungeonCatalog.SanctuaryId;
            test._arena = Array.IndexOf(args, "-onlinearena") >= 0;
            test._switch = Array.IndexOf(args, "-onlineswitch") >= 0;
            var perf = Array.IndexOf(args, "-onlineperf");
            if (perf >= 0) test._perfSeconds = perf + 1 < args.Length && float.TryParse(args[perf + 1], out var s) ? s : 20f;
            DontDestroyOnLoad(test.gameObject);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            _report = new StreamWriter(Path.Combine(_folder, "relatorio.txt"));

            var loginGiveUp = Time.realtimeSinceStartup + 20f;
            while (!GameMode.IsOnline && Time.realtimeSinceStartup < loginGiveUp) yield return null;
            if (!GameMode.IsOnline)
            {
                Finish("FALHA: o login automático não terminou");
                yield break;
            }
            yield return null; // deixa a cena recarregada iniciar
            Log($"Teste online: {GameMode.PlayerName} (personagem {GameMode.CharacterId}) -> {GameMode.Host}:{GameMode.Port}");

            var giveUpAt = Time.realtimeSinceStartup + 15f;
            NetworkSession session = null;
            while (Time.realtimeSinceStartup < giveUpAt)
            {
                session = NetworkSession.Instance;
                if (session != null && !string.IsNullOrEmpty(session.Error)) break;
                if (session != null && session.IsWelcomed && session.Clock.IsSynced && session.Views.Count > 0) break;
                yield return null;
            }

            if (session == null || !session.IsWelcomed)
            {
                Finish($"FALHA: não entrou no mundo ({session?.Error ?? "sem NetworkSession"})");
                yield break;
            }

            // Dá tempo de chegarem alguns snapshots e a interpolação andar.
            yield return new WaitForSecondsRealtime(1.5f);

            var monsters = 0;
            var players = 0;
            foreach (var view in session.Views.Values)
            {
                if (view.Kind == EntityKind.Monster) monsters++;
                else players++;
            }
            Log($"Entrou como id {session.PlayerId}; RTT {session.Clock.LastRtt * 1000:0} ms; offset {session.Clock.Offset:0.000} s");
            if (PlayerCombat.Local != null)
            {
                var me = PlayerCombat.Local;
                var progress = me.GetComponent<PlayerProgression>().Progress;
                var bag = me.GetComponent<PlayerLoot>();
                var count = 0;
                foreach (var stack in bag.Inventory) count += stack.Quantity;
                Log($"Personagem ao entrar: Nv {progress.Level}, XP {progress.Xp}, Alz {bag.Alz}, itens {count}, HP {me.Hp.Current}/{me.Hp.Max}");
            }
            Log($"Vendo {monsters} monstros e {players} outros jogadores");
            Log($"Música tocando: {Game.Client.Fx.MusicPlayer.Playing}; ambiente: {Game.Client.Fx.AmbientPlayer.Describe()}");

            // Tutorial do começo (o personagem de teste sempre recomeça do primeiro passo).
            Tutorial.Restart();
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("0-tutorial");
            yield return new WaitForSecondsRealtime(0.3f); // a próxima foto não pode cair no mesmo quadro

            // Seleciona o monstro mais perto para a foto mostrar o anel e a plaquinha.
            var selector = PlayerCombat.Local != null ? PlayerCombat.Local.GetComponent<TargetSelector>() : null;
            if (selector != null) selector.SelectNext();

            Capture("1-online");
            yield return new WaitForEndOfFrame();
            yield return null;

            var fightResult = "";
            if (_fight)
            {
                var outcome = new FightOutcome();
                yield return Fight(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult = "; " + outcome.Message;
            }

            if (_uiTour)
            {
                var outcome = new FightOutcome();
                yield return UiTour(outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_social)
            {
                var outcome = new FightOutcome();
                yield return Social(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_dungeon)
            {
                var outcome = new FightOutcome();
                yield return DungeonRun(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_arena)
            {
                var outcome = new FightOutcome();
                yield return ArenaRun(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_perfSeconds > 0)
            {
                var outcome = new FightOutcome();
                yield return Performance(session, outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            if (_stayOnline > 0)
            {
                Log($"Ficando online por {_stayOnline:0} s");
                var maxPlayers = 0;
                var until = Time.realtimeSinceStartup + _stayOnline;
                while (Time.realtimeSinceStartup < until)
                {
                    var seen = 0;
                    foreach (var view in session.Views.Values) if (view.Kind == EntityKind.Player) seen++;
                    maxPlayers = Mathf.Max(maxPlayers, seen);
                    yield return null;
                }
                players = Mathf.Max(players, maxPlayers);
                Log($"Enquanto online viu até {maxPlayers} outros jogadores");
                Capture("4-antes-de-sair");
                yield return new WaitForEndOfFrame();
                yield return null;
            }

            // Por último: a troca recarrega a cena e a sessão de cima deixa de existir.
            if (_switch)
            {
                var outcome = new FightOutcome();
                yield return SwitchCharacter(outcome);
                if (!outcome.Ok)
                {
                    Finish("FALHA: " + outcome.Message);
                    yield break;
                }
                fightResult += "; " + outcome.Message;
            }

            Finish(monsters > 0
                ? $"SUCESSO: online com {monsters} monstros e {players} jogadores à vista{fightResult}"
                : "FALHA: nenhum monstro recebido do servidor");
        }

        /// <summary>
        /// Trocar de personagem sem fechar o jogo: sai do mundo para o menu (a conexão fecha e o servidor salva) e entra
        /// de novo. Com conta, o menu abre a lista de personagens; o teste entra como convidado, então volta pelo
        /// mesmo caminho do menu (outra escolha e a cena recarregada).
        /// </summary>
        private IEnumerator SwitchCharacter(FightOutcome outcome)
        {
            var name = GameMode.PlayerName;
            var host = GameMode.Host;
            var port = GameMode.Port;
            OptionsWindow.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            OptionsWindow.SwitchCharacter();
            yield return WaitFor(() => ConnectMenu.IsOpen && NetworkSession.Instance == null && FindAnyObjectByType<ConnectMenu>() != null, 5f);
            if (!ConnectMenu.IsOpen || NetworkSession.Instance != null)
            {
                outcome.Message = "a troca de personagem não voltou para o menu";
                yield break;
            }
            Log("Troca: saiu do mundo e o menu de entrada abriu (a conexão fechou)");
            yield return new WaitForSecondsRealtime(1.5f);
            Capture("2-troca-menu");
            yield return new WaitForEndOfFrame();

            GameMode.SetOnline(host, port, name);
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            yield return WaitFor(() => NetworkSession.Instance != null && NetworkSession.Instance.IsWelcomed && NetworkSession.Instance.Views.Count > 0, 15f);
            if (NetworkSession.Instance == null || !NetworkSession.Instance.IsWelcomed)
            {
                outcome.Message = "não entrou de novo depois da troca";
                yield break;
            }
            Log($"Troca: entrou de novo como id {NetworkSession.Instance.PlayerId}");
            yield return new WaitForSecondsRealtime(1f);
            Capture("2b-troca-de-volta");
            yield return new WaitForEndOfFrame();
            outcome.Ok = true;
            outcome.Message = "trocou de personagem e voltou ao mundo";
        }

        private sealed class FightOutcome
        {
            public bool Ok;
            public string Message = "";
        }

        /// <summary>
        /// Anda até um coelho (velocidade de corrida, que o servidor aceita), faz um combo com 3 acertos no centro da
        /// janela, termina de matar e confere o que o servidor decidiu: notas do combo, XP, Alz e coleta do drop.
        /// </summary>
        private IEnumerator Fight(NetworkSession session, FightOutcome outcome)
        {
            var combat = PlayerCombat.Local;
            var motor = combat.GetComponent<PlayerMotor>();
            var selector = combat.GetComponent<TargetSelector>();
            var loot = combat.GetComponent<PlayerLoot>();
            var grades = new List<byte>();
            var damageDealt = 0;
            void OnMessage(INetMessage message)
            {
                if (message is CombatEvent hit && hit.AttackerId == session.PlayerId)
                {
                    damageDealt += hit.Damage;
                    if (hit.Grade != 255) grades.Add(hit.Grade);
                }
            }
            session.MessageReceived += OnMessage;
            var xpBefore = combat.GetComponent<PlayerProgression>().Progress.Xp;
            var alzBefore = loot.Alz;

            // Caça até cair algum item (cerca de 1 em 3 coelhos não derruba nada), no máximo 4 coelhos.
            // O combo é feito só no primeiro.
            var kills = 0;
            NetEntityView rabbit = null;
            for (var round = 0; round < 4; round++)
            {
                rabbit = null;
                foreach (var view in session.Views.Values)
                {
                    if (view.TypeId != "coelho_saltador" || view.IsDead) continue;
                    if (rabbit == null || Distance(view, motor) < Distance(rabbit, motor)) rabbit = view;
                }
                if (rabbit == null)
                {
                    outcome.Message = "nenhum coelho à vista";
                    yield break;
                }
                Log($"Indo até o coelho {rabbit.Id} a {Distance(rabbit, motor):0} m");

                // Anda a 7 m/s (abaixo do limite do servidor) até ficar a 1,8 m.
                var giveUpAt = Time.realtimeSinceStartup + 30f;
                while (Distance(rabbit, motor) > 1.8f && Time.realtimeSinceStartup < giveUpAt)
                {
                    var direction = rabbit.transform.position - motor.transform.position;
                    direction.y = 0;
                    motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.7f, 7f * Time.deltaTime));
                    yield return null;
                }
                selector.Select(rabbit.Target);
                yield return new WaitForSecondsRealtime(0.3f);

                if (round == 0)
                {
                    // Combo: espera o marcador no centro da janela e aperta a skill 1.
                    combat.RequestStartCombo();
                    for (var i = 0; i < 3 && combat.Combo.State == Shared.Combat.ComboState.Active && !rabbit.IsDead; i++)
                    {
                        while (combat.Combo.State == Shared.Combat.ComboState.Active &&
                               combat.Combo.GetCursor(PlayerCombat.ComboNow) < combat.Combo.Config.WindowCenter)
                        {
                            Follow(motor, rabbit); // coelhos passeiam: acompanha como um jogador faria
                            yield return null;
                        }
                        if (combat.Combo.State != Shared.Combat.ComboState.Active) break;
                        combat.RequestSkill(0);
                        yield return null;
                    }
                    Capture("2-combo-online");
                    Log($"Música no campo: {Game.Client.Fx.MusicPlayer.Playing}; ambiente: {Game.Client.Fx.AmbientPlayer.Describe()}");
                    yield return new WaitForEndOfFrame();
                }

                // Termina de matar fora do combo, se precisar.
                giveUpAt = Time.realtimeSinceStartup + 20f;
                while (!rabbit.IsDead && Time.realtimeSinceStartup < giveUpAt)
                {
                    Follow(motor, rabbit);
                    if (selector.Current != rabbit.Target) selector.Select(rabbit.Target);
                    if (combat.CooldownRemaining(0) <= 0) combat.RequestSkill(0);
                    yield return null;
                }
                if (!rabbit.IsDead) break;
                kills++;
                yield return new WaitForSecondsRealtime(0.8f);
                Log($"Coelho {kills} morto; itens no chão {LootPickup.All.Count}");
                if (LootPickup.All.Count > 0 || loot.Inventory.Count > 0) break;
            }
            session.MessageReceived -= OnMessage;

            var xpAfter = combat.GetComponent<PlayerProgression>().Progress.Xp;
            Log($"Dano confirmado pelo servidor {damageDealt}; notas do combo [{string.Join(",", grades)}]");
            Log($"XP {xpBefore} -> {xpAfter}; Alz {alzBefore} -> {loot.Alz}; itens no chão {LootPickup.All.Count}");
            Capture("3-drop-online");
            yield return new WaitForEndOfFrame();

            // Coleta: vai até cada item vindo do servidor e aperta a tecla de pegar (o servidor confirma).
            var pickupGiveUp = Time.realtimeSinceStartup + 10f;
            while (LootPickup.All.Count > 0 && Time.realtimeSinceStartup < pickupGiveUp)
            {
                var item = LootPickup.All[0];
                var offset = item.transform.position - motor.transform.position;
                offset.y = 0;
                motor.Teleport(motor.transform.position + offset.normalized * Mathf.Min(offset.magnitude, 7f * Time.deltaTime));
                if (offset.magnitude < 1f) loot.PickupNearest();
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            var items = 0;
            foreach (var stack in loot.Inventory) items += stack.Quantity;

            // Ataque automático: com a opção ligada e um coelho selecionado, ele morre sem nenhum aperto de skill.
            NetEntityView autoTarget = null;
            foreach (var view in session.Views.Values)
                if (view.TypeId == "coelho_saltador" && !view.IsDead && (autoTarget == null || Distance(view, motor) < Distance(autoTarget, motor)))
                    autoTarget = view;
            if (autoTarget != null)
            {
                var autoGiveUp = Time.realtimeSinceStartup + 30f;
                while (Distance(autoTarget, motor) > 1.8f && Time.realtimeSinceStartup < autoGiveUp)
                {
                    var direction = autoTarget.transform.position - motor.transform.position;
                    direction.y = 0;
                    motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.7f, 7f * Time.deltaTime));
                    yield return null;
                }
                selector.Select(autoTarget.Target);
                GameSettings.AutoAttack = true;
                while (!autoTarget.IsDead && Time.realtimeSinceStartup < autoGiveUp)
                {
                    Follow(motor, autoTarget);
                    if (selector.Current != autoTarget.Target) selector.Select(autoTarget.Target);
                    yield return null;
                }
                GameSettings.AutoAttack = false;
                Log(autoTarget.IsDead ? "Ataque automático: coelho morto sem apertar nenhuma skill" : "Ataque automático: o coelho não morreu");
                if (!autoTarget.IsDead)
                {
                    outcome.Message = "o ataque automático não matou o coelho";
                    yield break;
                }
            }

            var excellent = grades.FindAll(g => g == (byte)Shared.Combat.ComboGrade.Excellent).Count;
            // Personagem de conta novo ainda não tem o combo (libera na missão "A arte do combo"): aí não exige Excellent.
            var comboLocked = CharacterState.HasSheet && !CharacterState.Sheet.ComboUnlocked;
            outcome.Ok = kills > 0 && damageDealt > 0 && xpAfter > xpBefore && loot.Alz > alzBefore && (excellent > 0 || comboLocked) && items > 0;
            outcome.Message = $"{kills} coelho(s) mortos pelo servidor ({damageDealt} de dano), {(comboLocked ? "combo ainda bloqueado pela missão" : $"{excellent} Excellent confirmados")}, " +
                              $"XP +{xpAfter - xpBefore}, Alz +{loot.Alz - alzBefore}, {items} itens coletados";
        }

        /// <summary>Dá um passo (7 m/s) na direção do alvo se ele estiver a mais de 1,6 m.</summary>
        private static void Follow(PlayerMotor motor, NetEntityView target)
        {
            var direction = target.transform.position - motor.transform.position;
            direction.y = 0;
            if (direction.magnitude <= 1.6f) return;
            motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.5f, 7f * Time.deltaTime));
        }

        private static float Distance(NetEntityView view, PlayerMotor motor)
        {
            var offset = view.transform.position - motor.transform.position;
            offset.y = 0;
            return offset.magnitude;
        }

        /// <summary>
        /// Passeio pela interface online: inventário, ficha, conversa com o ancião, missão "Primeiros passos"
        /// (aceitar, falar com a ferreira, entregar) e a janela da ferreira — com screenshots de cada uma.
        /// </summary>
        private IEnumerator UiTour(FightOutcome outcome)
        {
            var windows = UI.GameWindows.Instance;
            var me = PlayerCombat.Local;
            if (windows == null || me == null)
            {
                outcome.Message = "janelas do jogo online não existem";
                yield break;
            }
            var giveUp = Time.realtimeSinceStartup + 5f;
            while (!CharacterState.HasSheet && Time.realtimeSinceStartup < giveUp) yield return null;
            if (!CharacterState.HasSheet)
            {
                outcome.Message = "a ficha do personagem não chegou";
                yield break;
            }
            var weapon = CharacterState.Equipment[Game.Shared.Items.EquipSlot.Weapon];
            Log($"Ficha: classe {CharacterState.Class}, ataque {CharacterState.Sheet.AttackPower}, combo liberado {CharacterState.Sheet.ComboUnlocked}; " +
                $"arma {weapon?.ItemId ?? "nenhuma"}, {CharacterState.Bag.FreeSlots} posições livres na bolsa");
            if (weapon == null)
            {
                outcome.Message = "personagem sem a arma inicial";
                yield break;
            }

            windows.SetInventoryOpen(true);
            windows.SetAttributesOpen(true);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("5-inventario");
            yield return new WaitForEndOfFrame();

            // Esconde o elmo pelo servidor (que devolve o EntityLook) e volta a mostrar.
            var combat = PlayerCombat.Local;
            var session = NetworkSession.Instance;
            var hidden = combat.HelmetHidden;
            session.Send(new AppearanceCommand { HideHelmet = !hidden });
            yield return WaitFor(() => combat.HelmetHidden != hidden, 3f);
            if (combat.HelmetHidden == hidden)
            {
                outcome.Message = "o servidor não trocou o elmo";
                yield break;
            }
            Capture("5b-elmo");
            yield return new WaitForEndOfFrame();
            session.Send(new AppearanceCommand { HideHelmet = hidden });
            Log($"Elmo: escondido {!hidden} e de volta");
            windows.SetInventoryOpen(false);
            windows.SetAttributesOpen(false);

            // Janela do personagem sozinha (P): o modelo com o que está vestido, as peças em volta e os atributos.
            windows.SetCharacterOpen(true);
            yield return new WaitForSecondsRealtime(0.6f);
            Capture("5c-personagem");
            yield return new WaitForEndOfFrame();
            windows.SetCharacterOpen(false);

            var quest = "primeiros_passos";
            if (!CharacterState.QuestCompleted(quest))
            {
                yield return WalkTo(NpcCatalog.Elder);
                windows.TalkToNearestNpc();
                yield return WaitFor(() => CharacterState.Dialog != null, 3f);
                Capture("6-anciao");
                yield return new WaitForEndOfFrame();
                if (CharacterState.Dialog == null || !CharacterState.Dialog.Offered.Contains(quest))
                {
                    outcome.Message = "o ancião não ofereceu a missão";
                    yield break;
                }
                NetworkSession.Instance.Send(new QuestCommand { Action = QuestActionKind.Accept, QuestId = quest });
                yield return WaitFor(() => CharacterState.CountsOf(quest) != null, 3f);

                yield return WalkTo(NpcCatalog.Smith);
                windows.TalkToNearestNpc();
                yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.Smith, 3f);
                windows.SelectForSmith(weapon.Uid);
                yield return new WaitForSecondsRealtime(0.5f);
                Capture("7-ferreira");
                yield return new WaitForEndOfFrame();

                yield return WalkTo(NpcCatalog.Elder);
                windows.TalkToNearestNpc();
                yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.ReadyToTurnIn.Contains(quest), 3f);
                NetworkSession.Instance.Send(new QuestCommand { Action = QuestActionKind.TurnIn, QuestId = quest });
                yield return WaitFor(() => CharacterState.QuestCompleted(quest), 3f);
                if (!CharacterState.QuestCompleted(quest))
                {
                    outcome.Message = "a missão não foi concluída";
                    yield break;
                }
            }

            yield return new WaitForSecondsRealtime(0.5f);
            Capture("8-missao-concluida");
            yield return new WaitForEndOfFrame();

            // Mestre de skills e a barra de atalhos arrumada pelo jogador (o servidor confirma na ficha).
            yield return WalkTo(NpcCatalog.Trainer);
            windows.TalkToNearestNpc();
            yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.Trainer, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("8b-mestre");
            yield return new WaitForEndOfFrame();
            CharacterState.CloseDialog();
            var original = new List<byte>(CharacterState.Sheet.Hotbar);
            var swapped = new List<byte>(original);
            (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
            // A skill da casa 8 vai para a segunda fileira (Alt+3).
            swapped[12] = swapped[7];
            swapped[7] = Game.Shared.Progression.SkillTraining.Empty;
            NetworkSession.Instance.Send(new SetHotbar { Slots = swapped });
            yield return WaitFor(() => CharacterState.Sheet.Hotbar.Count == SkillTraining.HotbarSize && CharacterState.Sheet.Hotbar[0] == original[1]
                && CharacterState.Sheet.Hotbar[7] == Game.Shared.Progression.SkillTraining.Empty, 3f);
            if (CharacterState.HotbarSkill(0) != original[1] || CharacterState.HotbarSkill(7) != -1 || CharacterState.HotbarSkill(12) != original[7])
            {
                outcome.Message = "a barra de atalhos não foi arrumada pelo servidor";
                yield break;
            }
            windows.SetSkillsOpen(true);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("8c-skills");
            yield return new WaitForEndOfFrame();
            windows.SetSkillsOpen(false);
            NetworkSession.Instance.Send(new SetHotbar { Slots = original });
            yield return WaitFor(() => CharacterState.HotbarSkill(7) == original[7], 3f);

            // Mercado da escrivã: põe uma poção à venda, a oferta aparece no quadro, retira e a poção volta.
            yield return WalkTo(NpcCatalog.Market);
            windows.TalkToNearestNpc();
            yield return WaitFor(() => CharacterState.Market != null && CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.Market, 3f);
            var potions = CharacterState.Bag.Count("pocao_hp_p");
            var potionSlot = -1;
            foreach (var (bagSlot, bagItem) in CharacterState.Bag.Items())
                if (bagItem.ItemId == "pocao_hp_p") potionSlot = bagSlot;
            if (CharacterState.Market == null || potionSlot < 0)
            {
                outcome.Message = "o mercado não abriu (ou não há poção para vender)";
                yield break;
            }
            windows.SelectForMarket(CharacterState.Bag[potionSlot].Uid, 120);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("8e-mercado-vender");
            yield return new WaitForEndOfFrame();
            // Até 3 tentativas: o andar do teste manda muitas mensagens e o limite por conexão pode descartar uma.
            var attempts = 0;
            while (attempts < 3 && CharacterState.Market.Mine.Count == 0)
            {
                attempts++;
                NetworkSession.Instance.Send(new MarketCommand { Action = MarketActionKind.Sell, Slot = (ushort)potionSlot, Quantity = 1, Price = 120 });
                yield return WaitFor(() => CharacterState.Market.Mine.Count == 1 && CharacterState.Bag.Count("pocao_hp_p") == potions - 1, 3f);
            }
            if (CharacterState.Market.Mine.Count != 1 || CharacterState.Bag.Count("pocao_hp_p") != potions - 1)
            {
                outcome.Message = "a oferta não apareceu no mercado";
                yield break;
            }
            Log($"Mercado: 1 poção à venda por 120 Alz (oferta {CharacterState.Market.Mine[0].Id}, tentativa {attempts}); bolsa {potions} -> {potions - 1}");
            windows.ShowMarketOffers();
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("8f-mercado-quadro");
            yield return new WaitForEndOfFrame();
            NetworkSession.Instance.Send(new MarketCommand { Action = MarketActionKind.Cancel, ListingId = CharacterState.Market.Mine[0].Id });
            yield return WaitFor(() => CharacterState.Market.Mine.Count == 0 && CharacterState.Bag.Count("pocao_hp_p") == potions, 3f);
            if (CharacterState.Bag.Count("pocao_hp_p") != potions)
            {
                outcome.Message = "a poção não voltou ao retirar a oferta";
                yield break;
            }
            Log("Mercado: oferta retirada, a poção voltou para a bolsa");
            CharacterState.CloseDialog();

            // Tela de progressão (L): todas as barras e o próximo objetivo de cada uma.
            windows.SetProgressOpen(true);
            yield return new WaitForSecondsRealtime(0.8f);
            Capture("8g-progresso");
            yield return new WaitForEndOfFrame();
            windows.SetProgressOpen(false);

            // O caçador do acampamento nos Campos do Vento (missões da região).
            yield return WalkTo(NpcCatalog.Hunter);
            windows.TalkToNearestNpc();
            yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.Hunter, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("8d-cacador");
            yield return new WaitForEndOfFrame();
            if (CharacterState.Dialog == null || CharacterState.Dialog.NpcId != NpcCatalog.Hunter)
            {
                outcome.Message = "o Caçador Rurik não abriu a conversa";
                yield break;
            }
            CharacterState.CloseDialog();

            // Painel de missões com o destino ("→ Coelho Saltador, Campos do Vento") e o anel no minimapa.
            NetworkSession.Instance.Send(new QuestCommand { Action = QuestActionKind.Accept, QuestId = "diaria_coelhos" });
            yield return WaitFor(() => CharacterState.CountsOf("diaria_coelhos") != null, 3f);
            var daily = CharacterState.CountsOf("diaria_coelhos");
            if (daily == null || QuestGuide.For(QuestCatalog.Get("diaria_coelhos"), daily) == null)
            {
                outcome.Message = "a diária do caçador não foi aceita ou não tem destino";
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("8d2-missao-destino");
            yield return new WaitForEndOfFrame();
            Log("Missão aceita com destino no painel e no minimapa");

            // Janela de opções (F10) por cima do jogo.
            OptionsWindow.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("9-opcoes");
            yield return new WaitForEndOfFrame();
            OptionsWindow.OpenQuitConfirm();
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("9-sair");
            yield return new WaitForEndOfFrame();
            OptionsWindow.Open();
            OptionsWindow.Open(OptionsWindow.Page.Keys);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("9b-atalhos");
            yield return new WaitForEndOfFrame();
            OptionsWindow.Open(OptionsWindow.Page.Controls);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("9c-controles");
            yield return new WaitForEndOfFrame();
            OptionsWindow.Close();

            // Mapa grande (tecla M) e o minimapa no canto.
            Minimap.BigOpen = true;
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("9c-mapa");
            yield return new WaitForEndOfFrame();
            Minimap.BigOpen = false;

            // Efeitos: item de cada raridade no chão, subida de nível e melhoria (só na tela, nada vai ao servidor).
            var loot = me.GetComponent<PlayerLoot>();
            var forward = me.transform.forward;
            var right = me.transform.right;
            var previews = new List<Game.Client.Loot.LootPickup>();
            var rarities = new[] { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Epic };
            for (var i = 0; i < rarities.Length; i++)
            {
                var spot = me.transform.position + forward * 4f + right * (i - 1.5f) * 1.6f;
                previews.Add(loot.SpawnPreview(new ItemDrop("fragmento_guardiao", "", rarities[i], 1), spot));
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Capture("9d-raridade");
            yield return new WaitForSecondsRealtime(0.3f);
            Game.Client.Fx.ItemFx.LevelUp(me.transform);
            yield return new WaitForSecondsRealtime(0.6f);
            Capture("9e-nivel");
            yield return new WaitForSecondsRealtime(1.5f);
            Game.Client.Fx.ItemFx.UpgradeResult(me.transform, success: true, downgraded: false, newLevel: 8);
            yield return new WaitForSecondsRealtime(0.25f);
            Capture("9f-melhoria");
            yield return new WaitForSecondsRealtime(0.3f);
            foreach (var preview in previews) if (preview != null) Destroy(preview.gameObject);

            // Sensação das skills: cada skill das duas classes dispara o efeito dela num alvo de mentira à frente
            // (só na tela). As fotos pegam o meio das skills pesadas de cada classe.
            var dummy = new GameObject("Alvo de teste").transform;
            dummy.position = me.transform.position + me.transform.forward * 7f;
            foreach (var characterClass in Game.Shared.Accounts.ClassInfo.All)
            {
                foreach (var classSkill in ClassCatalog.SkillsFor(characterClass))
                {
                    var skill = classSkill.Skill;
                    Game.Client.Fx.SkillFx.OnCast(skill, me.transform);
                    var landed = !Game.Client.Fx.SkillFx.IsProjectile(skill);
                    if (!landed) Game.Client.Fx.SkillFx.Launch(skill, me.transform, dummy, Vector3.up, null, () => landed = true);
                    yield return WaitFor(() => landed, 2f);
                    Game.Client.Fx.SkillFx.OnImpact(skill, dummy.position + Vector3.up, Game.Shared.Combat.ComboGrade.Excellent, false);
                    if (skill.Id == "execucao") Capture("9g-skill-execucao");
                    if (skill.Id == "fenda_sismica") Capture("9h-skill-fenda");
                    if (skill.Id == "cataclismo") Capture("9i-skill-cataclismo");
                    yield return new WaitForSecondsRealtime(0.45f);
                }
            }
            Destroy(dummy.gameObject);
            outcome.Ok = true;
            outcome.Message = $"missão \"{QuestCatalog.Get(quest).Name}\" concluída, Nv {me.Level}, {CharacterState.Bag.Count("pocao_hp_p")} poções";
        }

        /// <summary>Anda até perto do NPC no ritmo de caminhada (o servidor recusa teleporte).</summary>
        /// <summary>
        /// Espera o convite de grupo de um bot (Game.Bots --invite Nome), aceita, manda mensagens no grupo e no mapa
        /// (uma com palavrão, que o servidor filtra) e confere o quadro do grupo, o chat e o sussurro do bot.
        /// </summary>
        private IEnumerator Social(NetworkSession session, FightOutcome outcome)
        {
            var social = UI.SocialWindows.Instance;
            if (social == null)
            {
                outcome.Message = "sem SocialWindows";
                yield break;
            }
            yield return WaitFor(() => social.PendingInvite != null, 30f);
            if (social.PendingInvite == null)
            {
                outcome.Message = "nenhum convite de grupo chegou";
                yield break;
            }
            Log($"Convite de {social.PendingInvite.InviterName}");
            Capture("5-convite");
            yield return new WaitForEndOfFrame();
            yield return null;

            social.AcceptInvite();
            yield return WaitFor(() => social.Party.Members.Count >= 2, 5f);
            if (social.Party.Members.Count < 2)
            {
                outcome.Message = "aceitou o convite mas o grupo não chegou";
                yield break;
            }
            social.Submit("/g oi, cheguei!");
            social.Submit("/m que porra de lobo");
            UI.GameInput.Typing = true; // mostra a caixa do chat aberta na foto
            yield return WaitFor(() => Contains(social, "bem-vindo ao grupo") && Contains(social, "sussurro do bot")
                                      && Contains(social, "oi, cheguei") && Contains(social, "que *****"), 8f);
            var ok = Contains(social, "bem-vindo ao grupo") && Contains(social, "sussurro do bot") && Contains(social, "que *****");
            Capture("6-grupo-e-chat");
            yield return new WaitForEndOfFrame();
            yield return null;
            UI.GameInput.Typing = false;
            foreach (var line in social.RecentLines) Log("Chat: " + line);
            if (!ok)
            {
                outcome.Message = "o chat não recebeu as mensagens esperadas";
                yield break;
            }
            // Amigos, bloqueio e denúncia (o bot precisa ter entrado com conta: Game.Bots --accounts).
            var inviter = social.Party.Members.Find(m => m.Id != session.PlayerId).Name;
            social.Submit($"/amigo {inviter}");
            yield return WaitFor(() => social.Lists.Friends.Exists(f => f.Name == inviter && f.Online), 5f);
            if (!social.Lists.Friends.Exists(f => f.Name == inviter && f.Online))
            {
                outcome.Message = $"{inviter} não apareceu online na lista de amigos";
                yield break;
            }
            social.SetFriendsOpen(true);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("7-amigos");
            yield return new WaitForEndOfFrame();
            yield return null;

            social.Submit($"/bloquear {inviter}");
            yield return WaitFor(() => social.Lists.Blocked.Contains(inviter), 5f);
            var blocked = social.Lists.Blocked.Contains(inviter) && !social.Lists.Friends.Exists(f => f.Name == inviter);
            Capture("8-bloqueado");
            yield return new WaitForEndOfFrame();
            yield return null;
            social.Submit($"/desbloquear {inviter}");
            yield return WaitFor(() => social.Lists.Blocked.Count == 0, 5f);
            social.Submit($"/denunciar {inviter} teste automático de denúncia");
            yield return WaitFor(() => Contains(social, $"Denúncia de {inviter} enviada"), 5f);
            social.SetFriendsOpen(false);
            if (!blocked || social.Lists.Blocked.Count != 0 || !Contains(social, $"Denúncia de {inviter} enviada"))
            {
                outcome.Message = "bloquear, desbloquear ou denunciar não funcionou";
                yield break;
            }

            // Loja: vende 4 poções (4 x 10 Alz) e compra 1 (40 Alz).
            yield return WalkTo(NpcCatalog.Merchant);
            var windows = UI.GameWindows.Instance;
            windows.TalkToNearestNpc();
            yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.Merchant, 3f);
            var potions = CharacterState.Bag.Count("pocao_hp_p");
            var potionSlot = FirstSlotOf("pocao_hp_p");
            if (CharacterState.Dialog == null || potionSlot < 0)
            {
                outcome.Message = "não abriu a loja ou não há poções na bolsa";
                yield break;
            }
            windows.SelectForSale(CharacterState.Bag[potionSlot].Uid);
            session.Send(new ShopCommand { Action = ShopActionKind.Sell, Slot = (ushort)potionSlot, Quantity = 4 });
            yield return WaitFor(() => CharacterState.Bag.Count("pocao_hp_p") == potions - 4, 3f);
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("9-loja");
            yield return new WaitForEndOfFrame();
            yield return null;
            session.Send(new ShopCommand { Action = ShopActionKind.Buy, ItemId = "pocao_hp_p", Quantity = 1 });
            yield return WaitFor(() => CharacterState.Bag.Count("pocao_hp_p") == potions - 3, 3f);
            if (CharacterState.Bag.Count("pocao_hp_p") != potions - 3)
            {
                outcome.Message = $"loja: esperava {potions - 3} poções, tem {CharacterState.Bag.Count("pocao_hp_p")}";
                yield break;
            }
            CharacterState.CloseDialog();

            // Troca: o bot (perto da mercadora) aceita, oferece 2 poções e confirma quando a gente confirma.
            social.Submit($"/trocar {inviter}");
            yield return WaitFor(() => CharacterState.Trade != null && CharacterState.Trade.Active && CharacterState.Trade.TheirItems.Count > 0, 10f);
            if (CharacterState.Trade == null || !CharacterState.Trade.Active)
            {
                var last = new List<string>(social.RecentLines);
                outcome.Message = "a troca não abriu (chat: " + string.Join(" | ", last.GetRange(Math.Max(0, last.Count - 3), Math.Min(3, last.Count))) + ")";
                yield break;
            }
            windows.OfferInTrade(FirstSlotOf("pocao_hp_p"));
            yield return WaitFor(() => CharacterState.Trade.MyItems.Count > 0, 3f);
            session.Send(new TradeCommand { Action = TradeActionKind.Lock });
            yield return WaitFor(() => CharacterState.Trade.MyLocked && CharacterState.Trade.TheirLocked, 5f);
            Capture("10-troca");
            yield return new WaitForEndOfFrame();
            yield return null;
            session.Send(new TradeCommand { Action = TradeActionKind.Confirm });
            yield return WaitFor(() => !CharacterState.Trade.Active, 5f);
            if (CharacterState.Trade.Active || !Contains(social, $"Troca com {inviter} concluída"))
            {
                outcome.Message = "a troca não foi concluída";
                yield break;
            }

            // Duelo: o bot aceita; depois da contagem, um golpe nele e desistimos (perde quem desiste).
            social.Submit($"/duelo {inviter}");
            yield return WaitFor(() => CharacterState.Pvp is { Mode: PvpMode.Duel }, 5f);
            if (CharacterState.Pvp is not { Mode: PvpMode.Duel } duel)
            {
                outcome.Message = "o duelo não começou";
                yield break;
            }
            yield return WaitFor(() => session.ServerNow >= CharacterState.Pvp.StartsAt + 0.2, 6f);
            var opponent = session.FindView(duel.OpponentId);
            var combat = PlayerCombat.Local;
            var duelHit = false;
            var duelGiveUp = Time.realtimeSinceStartup + 15f;
            while (opponent != null && Time.realtimeSinceStartup < duelGiveUp && !duelHit)
            {
                var motorNow = combat.GetComponent<PlayerMotor>();
                if (Distance(opponent, motorNow) > 1.8f)
                {
                    var direction = opponent.transform.position - motorNow.transform.position;
                    direction.y = 0;
                    motorNow.Teleport(motorNow.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.7f, 6.5f * Time.deltaTime));
                    yield return null;
                    continue;
                }
                var selectorNow = combat.GetComponent<TargetSelector>();
                if (selectorNow.Current != opponent.Target) selectorNow.Select(opponent.Target);
                var before = opponent.Target.Hp.Current;
                if (combat.CooldownRemaining(0) <= 0) combat.RequestSkill(0);
                yield return new WaitForSecondsRealtime(0.4f);
                duelHit = opponent.Target.Hp.Current < before;
            }
            Capture("18-duelo");
            yield return new WaitForEndOfFrame();
            yield return null;
            session.Send(new DuelCommand { Action = DuelActionKind.Forfeit });
            yield return WaitFor(() => CharacterState.Pvp is { Mode: PvpMode.None }, 5f);
            if (!duelHit || CharacterState.Pvp is not { Mode: PvpMode.None })
            {
                outcome.Message = $"duelo: acertou {duelHit}, terminou {CharacterState.Pvp?.Mode == PvpMode.None}";
                yield break;
            }

            outcome.Ok = true;
            outcome.Message = $"grupo com {social.Party.Members.Count} ({string.Join(", ", social.Party.Members.ConvertAll(m => m.Name))}), " +
                              "chat, filtro, amigos, bloqueio, denúncia, loja, troca e duelo ok";
        }

        /// <summary>
        /// Entra sozinho na Cripta pelo portal (servidor com --guests --dungeon-test, cliente convidado com chaves),
        /// limpa as salas na ordem, derruba o chefe e confere o resultado, o ranking e a volta ao portal.
        /// </summary>
        private IEnumerator DungeonRun(NetworkSession session, FightOutcome outcome)
        {
            var combat = PlayerCombat.Local;
            var motor = combat.GetComponent<PlayerMotor>();
            var selector = combat.GetComponent<TargetSelector>();
            var windows = UI.GameWindows.Instance;
            var crypt = Shared.Dungeon.DungeonCatalog.Find(_dungeonId);

            yield return WalkTo(NpcCatalog.DungeonPortal);
            windows.TalkToNearestNpc();
            yield return WaitFor(() => CharacterState.Dialog != null && CharacterState.Dialog.NpcId == NpcCatalog.DungeonPortal, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("11-portal");
            yield return new WaitForEndOfFrame();
            yield return null;

            windows.EnterDungeon(_dungeonId);
            CharacterState.CloseDialog();
            yield return WaitFor(() => CharacterState.Dungeon != null, 5f);
            if (CharacterState.Dungeon == null)
            {
                outcome.Message = $"não entrou em {crypt.Name}";
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Log($"Na cripta: origem z {CharacterState.Dungeon.OriginZ}, posição {motor.transform.position}");
            Capture("12-cripta");
            Log($"Música na dungeon: {Game.Client.Fx.MusicPlayer.Playing}; ambiente: {Game.Client.Fx.AmbientPlayer.Describe()}");
            yield return new WaitForEndOfFrame();
            yield return null;

            var bossShot = false;
            var giveUp = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < giveUp && CharacterState.Dungeon is { Completed: false } state)
            {
                var room = crypt.Rooms[Mathf.Min(state.Room, crypt.Rooms.Count - 1)];
                NetEntityView target = null;
                foreach (var view in session.Views.Values)
                {
                    if (view.Kind != EntityKind.Monster || view.IsDead) continue;
                    if (!room.IsBossRoom && view.transform.position.z > state.OriginZ + room.GateZ) continue;
                    if (target == null || Distance(view, motor) < Distance(target, motor)) target = view;
                }
                if (target == null)
                {
                    // Nada à frente: anda para o meio da sala aberta.
                    var middle = new Vector3(state.OriginX, 0, state.OriginZ + (room.StartZ + room.EndZ) / 2);
                    var step = middle - motor.transform.position;
                    step.y = 0;
                    if (step.magnitude > 1f) motor.Teleport(motor.transform.position + step.normalized * Mathf.Min(step.magnitude, 6.5f * Time.deltaTime));
                    yield return null;
                    continue;
                }
                if (Distance(target, motor) > 1.8f)
                {
                    var direction = target.transform.position - motor.transform.position;
                    direction.y = 0;
                    motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.7f, 6.5f * Time.deltaTime));
                    yield return null;
                    continue;
                }
                if (selector.Current != target.Target) selector.Select(target.Target);
                if (combat.CooldownRemaining(0) <= 0) combat.RequestSkill(0);
                else if (combat.CooldownRemaining(1) <= 0) combat.RequestSkill(1);
                if (!bossShot && room.IsBossRoom && target.TypeId == crypt.BossId)
                {
                    bossShot = true;
                    yield return new WaitForSecondsRealtime(1f);
                    Capture("13-chefe");
                    Log($"Música no chefe: {Game.Client.Fx.MusicPlayer.Playing}");
                    yield return new WaitForEndOfFrame();
                }
                yield return new WaitForSecondsRealtime(0.15f);
            }

            var final = CharacterState.Dungeon;
            if (final == null || !final.Completed)
            {
                outcome.Message = $"não venceu {crypt.Name} (sala {(final?.Room ?? 0) + 1}, faltando {final?.MonstersLeft})";
                yield break;
            }
            yield return WaitFor(() => CharacterState.RankingOf(_dungeonId)?.Ranking.Count > 0, 3f);
            yield return new WaitForSecondsRealtime(0.8f);
            Capture("14-vitoria");
            yield return new WaitForEndOfFrame();
            yield return null;
            var time = Shared.Dungeon.DungeonRules.FormatTime(final.ElapsedSeconds);
            var medal = Shared.Dungeon.DungeonRules.MedalName((Shared.Dungeon.DungeonMedal)final.Medal);

            session.Send(new DungeonCommand { Action = DungeonActionKind.Leave });
            yield return WaitFor(() => CharacterState.Dungeon == null, 5f);
            yield return new WaitForSecondsRealtime(0.5f);
            var portal = NpcCatalog.Find(NpcCatalog.DungeonPortal);
            var home = new Vector3(portal.X, 0, portal.Z) - motor.transform.position;
            home.y = 0;
            if (CharacterState.Dungeon != null || home.magnitude > 6f)
            {
                outcome.Message = $"não voltou ao portal (a {home.magnitude:0} m)";
                yield break;
            }
            outcome.Ok = true;
            outcome.Message = $"{crypt.Name}: vitória em {time}, medalha {medal}, ranking com {CharacterState.RankingOf(_dungeonId).Ranking.Count} tempo(s), de volta ao portal";
        }

        /// <summary>
        /// Arena (servidor com --guests --arena-size 1 e um bot com --arena): entra na fila, vai para a base, toma o
        /// ponto livre, acerta o inimigo e sai da partida, conferindo a volta à vila.
        /// </summary>
        private IEnumerator ArenaRun(NetworkSession session, FightOutcome outcome)
        {
            var combat = PlayerCombat.Local;
            var motor = combat.GetComponent<PlayerMotor>();
            var selector = combat.GetComponent<TargetSelector>();
            var social = UI.SocialWindows.Instance;

            social.Submit("/arena");
            yield return WaitFor(() => CharacterState.Pvp is { Mode: PvpMode.Arena }, 30f);
            if (CharacterState.Pvp is not { Mode: PvpMode.Arena } pvp)
            {
                outcome.Message = "a partida da arena não começou (tem um bot na fila?)";
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);
            Capture("15-arena-base");
            yield return new WaitForEndOfFrame();
            yield return null;
            yield return WaitFor(() => session.ServerNow >= CharacterState.Pvp.StartsAt, 15f);

            // Procura o inimigo (alvo selecionável) e bate até derrubá-lo (o bot passivo não revida).
            var hitEnemy = false;
            var downed = false;
            var giveUp = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < giveUp && !downed)
            {
                NetEntityView enemy = null;
                foreach (var view in session.Views.Values)
                    if (view.Kind == EntityKind.Player && view.Target != null && view.Target.IsSelectable && !view.IsDead) enemy = view;
                if (enemy == null)
                {
                    yield return null;
                    continue;
                }
                if (Distance(enemy, motor) > 1.8f)
                {
                    var direction = enemy.transform.position - motor.transform.position;
                    direction.y = 0;
                    motor.Teleport(motor.transform.position + direction.normalized * Mathf.Min(direction.magnitude - 1.7f, 6.5f * Time.deltaTime));
                    yield return null;
                    continue;
                }
                if (selector.Current != enemy.Target) selector.Select(enemy.Target);
                var before = enemy.Target.Hp.Current;
                if (combat.CooldownRemaining(0) <= 0) combat.RequestSkill(0);
                yield return new WaitForSecondsRealtime(0.4f);
                hitEnemy |= enemy.Target.Hp.Current < before || enemy.IsDead;
                downed = enemy.IsDead;
            }
            Capture("16-arena-luta");
            yield return new WaitForEndOfFrame();
            yield return null;

            // Com o inimigo caído (renasce na base), toma o ponto livre mais perto.
            bool OwnsAny()
            {
                foreach (var p in CharacterState.Pvp.Points) if (p.Owner == CharacterState.Pvp.Team) return true;
                return false;
            }
            giveUp = Time.realtimeSinceStartup + 40f;
            while (Time.realtimeSinceStartup < giveUp && !OwnsAny())
            {
                var state = CharacterState.Pvp;
                var goal = Vector3.zero;
                var best = float.MaxValue;
                for (var i = 0; i < state.Points.Count; i++)
                {
                    if (state.Points[i].Owner == state.Team || state.Points[i].Contested) continue;
                    var point = new Vector3(state.OriginX + Shared.Pvp.ArenaRules.Points[i].X, 0, state.OriginZ + Shared.Pvp.ArenaRules.Points[i].Z);
                    // Evita o ponto onde o inimigo já está (a captura não anda com os dois times dentro).
                    var occupied = false;
                    foreach (var view in session.Views.Values)
                        if (view.Kind == EntityKind.Player && view.Target != null && view.Target.IsSelectable &&
                            (view.transform.position - point).magnitude < Shared.Pvp.ArenaRules.CaptureRadius + 2) occupied = true;
                    var d = (point - motor.transform.position).magnitude + (occupied ? 1000 : 0);
                    if (d < best)
                    {
                        best = d;
                        goal = point;
                    }
                }
                var step = goal - motor.transform.position;
                step.y = 0;
                if (best < float.MaxValue && step.magnitude > 1f)
                    motor.Teleport(motor.transform.position + step.normalized * Mathf.Min(step.magnitude, 6.5f * Time.deltaTime));
                yield return null;
            }
            var captured = OwnsAny();
            Capture("17-arena-ponto");
            yield return new WaitForEndOfFrame();
            yield return null;

            session.Send(new ArenaCommand { Action = ArenaActionKind.Leave });
            yield return WaitFor(() => CharacterState.Pvp is { Mode: PvpMode.None }, 5f);
            yield return new WaitForSecondsRealtime(0.5f);
            var home = Mathf.Abs(motor.transform.position.z) < 15 && Mathf.Abs(motor.transform.position.x) < 15;
            if (!captured || !hitEnemy || !home)
            {
                outcome.Message = $"arena: capturou {captured}, acertou o inimigo {hitEnemy}, voltou à vila {home}";
                yield break;
            }
            outcome.Ok = true;
            outcome.Message = "arena: entrou pela fila, tomou um ponto, acertou o inimigo e voltou à vila";
        }

        private static int FirstSlotOf(string itemId)
        {
            for (var i = 0; i < CharacterState.Bag.Size; i++)
                if (CharacterState.Bag[i] != null && CharacterState.Bag[i].ItemId == itemId) return i;
            return -1;
        }

        private static bool Contains(UI.SocialWindows social, string text)
        {
            foreach (var line in social.RecentLines) if (line.Contains(text)) return true;
            return false;
        }

        /// <summary>
        /// Medição de desempenho (-onlineperf segundos): vai para a entrada dos Campos do Vento olhando para o campo, onde
        /// os bots do --crowd caçam, tira o limite de FPS e mede os quadros. Relata FPS médio, o 1% pior e quantos
        /// jogadores e monstros estavam na tela.
        /// </summary>
        private IEnumerator Performance(NetworkSession session, FightOutcome outcome)
        {
            var me = PlayerCombat.Local;
            var motor = me.GetComponent<PlayerMotor>();
            var target = new Vector3(0, 0, 14f);
            var giveUp = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < giveUp)
            {
                var flat = target - motor.transform.position;
                flat.y = 0;
                if (flat.magnitude < 0.2f) break;
                motor.Teleport(motor.transform.position + Vector3.ClampMagnitude(flat, 6f * Time.deltaTime));
                yield return null;
            }
            motor.FacePoint(new Vector3(0, 0, 40f));
            var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (orbit != null) orbit.LookAlong(Vector3.forward);

            var previousRate = Application.targetFrameRate;
            var previousVSync = QualitySettings.vSyncCount;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            yield return new WaitForSecondsRealtime(2f); // estabiliza (GC, primeiras alocações)

            var frames = new List<float>();
            var onScreen = 0;
            var monstersOnScreen = 0;
            var gcBefore = GC.CollectionCount(0);
            long allocated = 0;
            var lastMemory = GC.GetTotalMemory(false);
            var until = Time.realtimeSinceStartup + _perfSeconds;
            var cam = Camera.main;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime);
                var memory = GC.GetTotalMemory(false);
                if (memory > lastMemory) allocated += memory - lastMemory; // queda = coleta; só conta o que subiu
                lastMemory = memory;
                var players = 0;
                var monsters = 0;
                foreach (var view in session.Views.Values)
                {
                    if (view == null || cam == null) continue;
                    var p = cam.WorldToViewportPoint(view.transform.position + Vector3.up);
                    if (p.z <= 0 || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) continue;
                    if (view.Kind == EntityKind.Player) players++;
                    else monsters++;
                }
                onScreen = Mathf.Max(onScreen, players);
                monstersOnScreen = Mathf.Max(monstersOnScreen, monsters);
            }
            var gcRuns = GC.CollectionCount(0) - gcBefore;
            Capture("perf");
            yield return new WaitForSecondsRealtime(0.3f);
            Application.targetFrameRate = previousRate;
            QualitySettings.vSyncCount = previousVSync;

            frames.Sort();
            var average = 0f;
            foreach (var f in frames) average += f;
            average /= Mathf.Max(1, frames.Count);
            var worst = frames.Count > 0 ? frames[Mathf.Clamp((int)(frames.Count * 0.99f), 0, frames.Count - 1)] : 0f;
            var max = frames.Count > 0 ? frames[frames.Count - 1] : 0f;
            Log($"Desempenho em {Screen.width}x{Screen.height}, qualidade {QualitySettings.names[QualitySettings.GetQualityLevel()]}: " +
                $"{1f / average:0} FPS médio, 1% pior {1f / worst:0} FPS ({worst * 1000:0.0} ms), pior quadro {max * 1000:0.0} ms, " +
                $"{frames.Count} quadros, {gcRuns} coletas de lixo, {allocated / 1024f / Mathf.Max(1, frames.Count):0.0} KB alocados por quadro, " +
                $"até {onScreen} jogadores e {monstersOnScreen} monstros na tela");
            // Onde está a alocação: mede 4 s com cada grupo de componentes da interface desligado.
            var groups = new (string Name, Behaviour[] Parts)[]
            {
                ("sem HUD", FindObjectsByType<CombatHud>()),
                ("sem janelas", FindObjectsByType<GameWindows>()),
                ("sem chat/grupo", FindObjectsByType<SocialWindows>()),
                ("sem números de dano", FindObjectsByType<DamageNumbers>()),
                ("sem tutorial", FindObjectsByType<Tutorial>()),
            };
            foreach (var (name, parts) in groups)
            {
                foreach (var part in parts) part.enabled = false;
                yield return new WaitForSecondsRealtime(0.5f);
                long phaseAllocated = 0;
                var phaseFrames = 0;
                var phaseLast = GC.GetTotalMemory(false);
                var phaseUntil = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < phaseUntil)
                {
                    yield return null;
                    phaseFrames++;
                    var memory = GC.GetTotalMemory(false);
                    if (memory > phaseLast) phaseAllocated += memory - phaseLast;
                    phaseLast = memory;
                }
                Log($"  {name}: {phaseAllocated / 1024f / Mathf.Max(1, phaseFrames):0.0} KB por quadro");
                foreach (var part in parts) part.enabled = true;
            }

            outcome.Ok = true;
            outcome.Message = $"desempenho: {1f / average:0} FPS médio, 1% pior {1f / worst:0} FPS com {onScreen} jogadores na tela";
        }

        private IEnumerator WalkTo(string npcId)
        {
            var npc = NpcCatalog.Find(npcId);
            var motor = PlayerCombat.Local.GetComponent<PlayerMotor>();
            var target = new Vector3(npc.X, 0, npc.Z - 2f);
            // NPC fora da vila: primeiro até a saída norte da praça (a reta pelo meio esbarra nos cantos dos NPCs).
            var flatStart = new Vector2(motor.transform.position.x, motor.transform.position.z);
            if ((new Vector2(target.x, target.z) - flatStart).magnitude > 14f && new Vector2(npc.X, npc.Z).magnitude > 16f)
                yield return WalkToPoint(motor, new Vector3(0f, 0, 12f));
            // Do outro lado do rio: atravessa pela ponte (a margem a pé pode ser íngreme demais para subir).
            var river = Game.Client.World.Ground.RiverZ(0f);
            if (target.z > river + 3f && motor.transform.position.z < river - 3f)
            {
                yield return WalkToPoint(motor, new Vector3(0f, 0, river - 7f));
                yield return WalkToPoint(motor, new Vector3(0f, 0, river + 7f));
            }
            yield return WalkToPoint(motor, target);
            yield return new WaitForSecondsRealtime(0.3f); // a última posição chega ao servidor
            Log($"Andou até {npcId}: está em ({motor.transform.position.x:0.0}, {motor.transform.position.z:0.0}), alvo ({target.x:0.0}, {target.z:0.0})");
        }

        private static IEnumerator WalkToPoint(PlayerMotor motor, Vector3 target)
        {
            var giveUp = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < giveUp)
            {
                var position = motor.transform.position;
                var flat = new Vector3(target.x - position.x, 0, target.z - position.z);
                if (flat.magnitude < 0.2f) break;
                motor.Teleport(position + Vector3.ClampMagnitude(flat, 6f * Time.deltaTime));
                yield return null;
            }
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            var giveUp = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < giveUp) yield return null;
        }

        private void Capture(string name)
        {
            var path = Path.Combine(_folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log($"Screenshot: {path}");
        }

        private void Log(string line)
        {
            Debug.Log("[OnlineTest] " + line);
            _report?.WriteLine(line);
            _report?.Flush();
        }

        private void Finish(string result)
        {
            Log(result);
            StartCoroutine(QuitSoon(result.StartsWith("SUCESSO") ? 0 : 1));
        }

        private IEnumerator QuitSoon(int code)
        {
            yield return new WaitForSecondsRealtime(1f);
            _report?.Dispose();
            _report = null;
            Application.Quit(code);
        }
    }
}
