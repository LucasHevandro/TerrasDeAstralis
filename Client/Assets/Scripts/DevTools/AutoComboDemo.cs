using System;
using System.Collections;
using System.IO;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Monsters;
using Game.Client.Player;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Demo automática para verificar o protótipo sem ninguém no teclado.
    /// Só roda se o executável for aberto com <c>-autodemo &lt;pasta de saída&gt;</c>:
    /// seleciona um alvo, faz um combo apertando no centro da janela, usa o finalizador,
    /// salva screenshots e um relatório na pasta indicada e fecha o jogo.
    /// </summary>
    public sealed class AutoComboDemo : MonoBehaviour
    {
        private const string Flag = "-autodemo";
        private const int HitsBeforeFinisher = 6;
        private static readonly int[] SkillPattern = { 0, 0, 2 };

        private string _outputFolder;
        private int _routesSeen;
        private StreamWriter _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, Flag);
            if (index < 0) return;

            var folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "autodemo");
            var host = new GameObject("AutoComboDemo");
            host.AddComponent<AutoComboDemo>()._outputFolder = folder;
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_outputFolder);
            _report = new StreamWriter(Path.Combine(_outputFolder, "relatorio.txt"));
            Log($"Demo iniciada em {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}");

            yield return new WaitForSecondsRealtime(1.5f);

            var combat = FindAnyObjectByType<PlayerCombat>();
            var selector = combat != null ? combat.GetComponent<TargetSelector>() : null;
            if (combat == null || selector == null)
            {
                Finish("FALHA: PlayerCombat ou TargetSelector não encontrados na cena");
                yield break;
            }

            // Aproxima o jogador do boneco mais perto para as skills corpo a corpo alcançarem.
            selector.SelectNext();
            var target = selector.Current;
            if (target == null)
            {
                Finish("FALHA: nenhum alvo selecionado");
                yield break;
            }
            Teleport(combat.transform, target.transform.position + (combat.transform.position - target.transform.position).normalized * 2f);
            Log($"Alvo: {target.DisplayName}, HP {target.Hp.Current}/{target.Hp.Max}, SP {combat.Sp.Current}");
            yield return new WaitForSecondsRealtime(0.5f);

            // Conjuração e fila: o Corte apertado no meio do Giro de Lâmina (0,55 s) espera ele acabar.
            combat.RequestSkill(1);
            yield return new WaitForSecondsRealtime(0.15f);
            combat.RequestSkill(0);
            var waited = combat.QueuedSkill == 0 && combat.Casting != null;
            Capture("0-conjurando");
            yield return new WaitForSecondsRealtime(0.8f);
            if (!waited || combat.QueuedSkill != -1)
            {
                Finish("FALHA: a skill pedida durante a conjuração não esperou na fila");
                yield break;
            }
            Log("Fila de skill: o Corte esperou o Giro de Lâmina acabar");
            yield return new WaitForSecondsRealtime(2f); // recarga do Giro antes do combo
            Capture("1-antes-do-combo");

            combat.RequestStartCombo();
            if (combat.Combo.State != ComboState.Active)
            {
                Finish($"FALHA: combo não iniciou ({combat.Message})");
                yield break;
            }
            Log($"Combo iniciado; SP agora {combat.Sp.Current}");

            var hpBefore = target.Hp.Current;
            while (combat.Combo.State == ComboState.Active && combat.Combo.HitCount < HitsBeforeFinisher)
            {
                // Espera o marcador chegar ao centro da janela e aperta a skill 1.
                while (combat.Combo.State == ComboState.Active &&
                       combat.Combo.GetCursor(PlayerCombat.ComboNow) < combat.Combo.Config.WindowCenter)
                    yield return null;
                if (combat.Combo.State != ComboState.Active) break;

                // Sequência 1, 1, 3 = físico, físico, mágico: deve fechar a rota "Tempestade".
                var skill = SkillPattern[combat.Combo.HitCount % SkillPattern.Length];
                var routeBefore = combat.LastRouteAt;
                combat.RequestSkill(skill);
                var route = combat.LastRouteAt > routeBefore ? $", rota {combat.LastRoute.Name}" : "";
                if (route.Length > 0) _routesSeen++;
                Log($"Aperto {combat.Combo.HitCount} (skill {skill + 1}): {combat.LastGrade}{route}, " +
                    $"janela {combat.Combo.WindowWidth:0.000}, barra {combat.Combo.BarDurationSeconds:0.000}s, " +
                    $"bônus {combat.Combo.ChainBonus:0.00}");
                if (combat.Combo.HitCount == 3) Capture("2-durante-o-combo");
                yield return null;
            }

            if (combat.Combo.HitCount < HitsBeforeFinisher)
            {
                Finish($"FALHA: combo terminou com {combat.Combo.HitCount} acertos ({combat.Combo.LastEndReason}; {combat.Message})");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.2f);
            combat.RequestFinisher();
            Log($"Finalizador: {combat.Message}; fim do combo = {combat.Combo.LastEndReason}");
            yield return new WaitForSecondsRealtime(0.15f);
            Capture("3-finalizador");

            var dealt = hpBefore - target.Hp.Current;
            var comboOk = combat.Combo.LastEndReason == ComboEndReason.Finished && dealt > 0 && _routesSeen > 0;
            if (!comboOk)
            {
                Finish($"FALHA: fim = {combat.Combo.LastEndReason}, rotas {_routesSeen}, dano {dealt}");
                yield break;
            }
            Log($"Combo OK: {HitsBeforeFinisher} acertos, {_routesSeen} rotas + finalizador, {dealt} de dano");

            yield return HuntDemo(combat, selector);
        }

        /// <summary>
        /// Fase 2: caça um coelho nos Campos do Vento, confere XP e drop, coleta os itens
        /// e fotografa as zonas do mapa.
        /// </summary>
        private IEnumerator HuntDemo(PlayerCombat combat, TargetSelector selector)
        {
            var progression = combat.GetComponent<PlayerProgression>();
            var loot = combat.GetComponent<PlayerLoot>();
            var monster = FindMonster("coelho_saltador", combat.transform.position);
            if (monster == null || progression == null || loot == null)
            {
                Finish("FALHA: coelho, PlayerProgression ou PlayerLoot não encontrados");
                yield break;
            }

            var levelBefore = progression.Level;
            var xpBefore = progression.Progress.Xp;
            var alzBefore = loot.Alz;
            var kills = 0;

            // Caça coelhos até cair algum item (cerca de 1 em 3 coelhos não derruba nada), no máximo 5.
            // (o jogador pode coletar sozinho passando por cima, por isso confere chão e inventário).
            while (kills < 5 && LootPickup.All.Count == 0 && loot.Inventory.Count == 0)
            {
                monster = FindMonster("coelho_saltador", combat.transform.position);
                if (monster == null) break;
                var monsterTarget = monster.GetComponent<Targetable>();
                Log($"Caçando {monster.Data.Name} (HP {monsterTarget.Hp.Current})");

                var giveUpAt = Time.realtimeSinceStartup + 25f;
                while (!monster.IsDead && Time.realtimeSinceStartup < giveUpAt)
                {
                    var offset = combat.transform.position - monster.transform.position;
                    offset.y = 0;
                    if (offset.magnitude > 2.2f || offset.magnitude < 0.5f)
                        Teleport(combat.transform, monster.transform.position + (offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.back) * 1.8f);
                    if (selector.Current != monsterTarget) selector.Select(monsterTarget);
                    if (combat.CooldownRemaining(0) <= 0) combat.RequestSkill(0);
                    yield return null;
                }

                if (!monster.IsDead)
                {
                    Finish($"FALHA: o coelho não morreu em 25 s (HP {monsterTarget.Hp.Current}; {combat.Message})");
                    yield break;
                }
                kills++;
                yield return new WaitForSecondsRealtime(0.6f);
                Log($"Coelho {kills} derrotado: XP {progression.Progress.Xp}, Alz {loot.Alz}, itens no chão {LootPickup.All.Count}");
            }

            yield return CaptureNow("4-monstro-derrotado");
            var xpGained = progression.Level > levelBefore || progression.Progress.Xp > xpBefore;
            var dropped = LootPickup.All.Count;
            Log($"Depois de {kills} coelhos: nível {levelBefore} -> {progression.Level}, XP {xpBefore} -> {progression.Progress.Xp}, " +
                $"Alz {alzBefore} -> {loot.Alz}, itens no chão {dropped}");

            // Coleta passando por cima de cada item.
            while (LootPickup.All.Count > 0)
            {
                Teleport(combat.transform, LootPickup.All[0].transform.position);
                yield return new WaitForSecondsRealtime(0.15f);
            }
            var items = 0;
            foreach (var stack in loot.Inventory) items += stack.Quantity;
            Log($"Inventário: {loot.Inventory.Count} tipos, {items} itens");

            // Fotos do mapa: monstros com plaquinhas na Mata Sombria e o chefe na arena.
            var boar = FindMonster("javali_furioso", combat.transform.position);
            if (boar != null)
            {
                Teleport(combat.transform, boar.transform.position + Vector3.back * 14f);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return CaptureNow("5-mata-sombria");
            }
            var boss = FindMonster("guardiao_das_ruinas", combat.transform.position);
            if (boss != null)
            {
                Teleport(combat.transform, boss.transform.position + Vector3.back * 12f);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return CaptureNow("6-arena-do-chefe");
            }

            var ok = xpGained && loot.Alz > alzBefore && items > 0 && boss != null;
            Finish(ok
                ? $"SUCESSO: combo com rotas e finalizador; {kills} coelhos com XP e Alz; {items} itens coletados; chefe na arena"
                : $"FALHA: XP {(xpGained ? "ok" : "não ganhou")}, Alz {loot.Alz - alzBefore}, drops {dropped}, " +
                  $"coletados {items}, chefe {(boss != null ? "ok" : "ausente")}");
        }

        /// <summary>Tira a screenshot e espera o quadro ser gravado antes de mudar a cena.</summary>
        private IEnumerator CaptureNow(string name)
        {
            Capture(name);
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        private static MonsterController FindMonster(string id, Vector3 near)
        {
            MonsterController best = null;
            var bestDistance = float.MaxValue;
            foreach (var monster in FindObjectsByType<MonsterController>())
            {
                if (monster.Data == null || monster.Data.Id != id || monster.IsDead) continue;
                var distance = Vector3.Distance(near, monster.transform.position);
                if (distance >= bestDistance) continue;
                best = monster;
                bestDistance = distance;
            }
            return best;
        }

        private static void Teleport(Transform player, Vector3 position)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.position = Game.Client.World.Ground.Snap(position) + Vector3.up * 0.05f;
            if (controller != null) controller.enabled = true;
        }

        private void Capture(string name)
        {
            var path = Path.Combine(_outputFolder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log($"Screenshot: {path}");
        }

        private void Log(string line)
        {
            Debug.Log("[AutoDemo] " + line);
            _report?.WriteLine(line);
            _report?.Flush();
        }

        private void Finish(string result)
        {
            Log(result);
            StartCoroutine(QuitSoon(result.StartsWith("SUCESSO") ? 0 : 1));
        }

        private IEnumerator QuitSoon(int exitCode)
        {
            // Dá tempo para a última screenshot ser gravada.
            yield return new WaitForSecondsRealtime(1f);
            _report?.Dispose();
            _report = null;
            Application.Quit(exitCode);
        }
    }
}
