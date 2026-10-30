using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkMomboMod
{
    [BepInPlugin("com.febraine.darkmombomod", "Dark Mombo Mod", "1.0.0")]
    public class PluginMain : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static PluginMain Instance;

        // Identifica a versão do código que está rodando (aparece no início do log). Mude ao fazer uma nova build.
        internal const string BuildTag = "texturas-v9";

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            string pluginDir = Path.GetDirectoryName(Info.Location); // pasta onde está a DarkMomboMod.dll
            Log.LogInfo("[Ghost] build '" + BuildTag + "' | DLL: " + Info.Location + " | gravada em " + File.GetLastWriteTime(Info.Location));
            GhostVFX.Init(pluginDir);
            GhostTextures.Init(pluginDir);
            if (GhostConfig.DebugLogs && GhostConfig.WriteDiagnosticFile) GhostDiagnostics.Init(pluginDir);

            Harmony harmony = new Harmony("com.febraine.darkmombomod");
            harmony.PatchAll();
            GhostPump.PatchEventSystem(harmony); // um relógio que roda em toda cena com UI, independente deste plugin
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(PumpLoop());          // mais um relógio (só roda se corrotinas do plugin funcionarem neste jogo)

            Log.LogInfo("Mombo Fantasma: patches aplicados.");
        }

        // Relógios que acionam as varreduras. Há vários de propósito: o log e o diagnóstico dizem quais funcionam neste jogo.
        private static IEnumerator PumpLoop()
        {
            while (true)
            {
                GhostPump.Pump("corrotina do plugin");
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        private void Update()
        {
            GhostPump.Pump("Update do plugin");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GhostDiagnostics.Shutdown();
        }

        // Trocar de cena destrói o MomboMovement (e as corrotinas dele sem rodar o finally),
        // então limpamos o estado estático aqui.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GhostState.ForceEnd();
            GhostDash.Reset();

            GhostTextures.Apply();
        }

    }

    // =====================================================================
    //  RELÓGIO DAS VARREDURAS
    //  As varreduras (texturas na memória, o que está na tela, relatórios agendados) são acionadas por VÁRIOS
    //  relógios: EventSystem.Update (todo jogo com UI), FixedUpdate do Mombo, Update e corrotina do plugin.
    //  Cada tarefa tem um horário próprio (tempo real), então roda no máximo uma vez por intervalo, venha o
    //  chamado de onde vier. A primeira chamada de cada relógio vai para o log e para o diagnóstico.
    // =====================================================================
    internal static class GhostPump
    {
        private static float nextTextureScan;
        private static float nextScreenScan;
        private static int textureScanPass;
        private static readonly HashSet<string> activeSources = new HashSet<string>();

        // Liga um postfix em EventSystem.Update. Fora do PatchAll, para uma falha aqui não derrubar os outros patches.
        public static void PatchEventSystem(Harmony harmony)
        {
            try
            {
                System.Reflection.MethodInfo target = AccessTools.Method(typeof(UnityEngine.EventSystems.EventSystem), "Update");
                System.Reflection.MethodInfo postfix = AccessTools.Method(typeof(GhostPump), "EventSystemPostfix");
                if (target == null || postfix == null)
                {
                    PluginMain.Log.LogWarning("[Ghost] relógio: EventSystem.Update não encontrado");
                    return;
                }

                harmony.Patch(target, null, new HarmonyMethod(postfix));
            }
            catch (System.Exception e)
            {
                PluginMain.Log.LogWarning("[Ghost] relógio: falha ao ligar no EventSystem: " + e.Message);
            }
        }

        public static void EventSystemPostfix()
        {
            Pump("EventSystem.Update");
        }

        public static void Pump(string source)
        {
            if (activeSources.Add(source))
            {
                PluginMain.Log.LogInfo("[Ghost] relógio ativo: " + source);
                GhostDiagnostics.Note("relógio ativo: " + source);
            }

            float now = Time.realtimeSinceStartup;

            if (now >= nextTextureScan)
            {
                nextTextureScan = now + GhostConfig.TextureScanInterval;
                try { GhostTextures.Apply(textureScanPass++); }
                catch (System.Exception e) { PluginMain.Log.LogError("[Ghost] texturas: erro na varredura: " + e); }
            }

            if (now >= nextScreenScan)
            {
                nextScreenScan = now + GhostConfig.ScreenScanInterval;
                try { GhostTextures.ScanOnScreen(); }
                catch (System.Exception e) { PluginMain.Log.LogError("[Ghost] texturas: erro na varredura da tela: " + e); }
            }

            GhostDiagnostics.PumpPending(now);
        }
    }

    // =====================================================================
    //  VALORES AJUSTÁVEIS
    // =====================================================================
    internal static class GhostConfig
    {
        // Troca de texturas do jogo (spritesheets do Mombo Primal etc.): PNGs em <pasta da DLL>/textures,
        // com o mesmo nome do Texture2D original (ex.: Mombo_Primal_Idle.png)
        public const bool ReplaceTextures = true;
        public const string TexturesFolder = "textures";
        public const float TextureScanInterval = 3f;            // segundos entre varreduras de texturas novas na memória
        public const bool LogTextureCandidates = false;          // loga as texturas "Mombo/Primal/SkillTree/Easter" encontradas na memória, com tamanho (diagnóstico)
        public const bool LogScreenTextures = false;             // loga cada textura nova que está sendo DESENHADA (sprites e UI ativos), com o objeto que a usa (diagnóstico)
        public const float ScreenScanInterval = 0.25f;          // segundos, em TEMPO REAL (ignora câmera lenta), entre varreduras do que está na tela
        public const bool WriteDiagnosticFile = false;           // grava ghost_diagnostico.txt (na pasta da DLL) no Combo, na morte e quando uma textura relevante nova aparece

        // VFX: PNGs colocados na mesma pasta da DarkMomboMod.dll
        public const string GhostSheetFile = "ghost_mombo.png";     // skin do modo fantasma
        public const int GhostFrameW = 98;                          // tamanho de CADA frame, em pixels (sheet 392x176 = 4x2)
        public const int GhostFrameH = 88;
        public const float GhostSheetFps = 10.67f;
        public const float GhostSheetPPUOverride = 0f;              // 0 = usa o mesmo pixels-per-unit do sprite original do Mombo
        public static readonly Color GhostTint = new Color(0.6f, 0.9f, 1f, 0.55f); // usado se não houver PNG

        public const string ExplosionSheetFile = "ghost_explosion.png";
        public const int ExplosionFrameW = 114;
        public const int ExplosionFrameH = 114;
        public const float ExplosionFps = 11.5f;

        // Dash
        public const float DashSpeedX = 12f;          // velocidade horizontal
        public const float DashSpeedY = 16f;          // velocidade para baixo
        public const float DashMaxTime = 1.5f;        // duração máxima
        public const float DashMinHoldTime = 0.08f;   // tempo mínimo antes de poder cancelar soltando o botão
        public const float DashCooldown = 0.35f;
        public const float ImpactLag = 0.12f;         // parada após o impacto
        public const int GraceTicks = 2;              // ticks de física ignorados no início
        public const int BlockedTicksNeeded = 2;      // ticks seguidos "travado" para contar como impacto
        public const float BlockedRatio = 0.35f;      // < 35% do deslocamento esperado em um eixo = bloqueado

        // Explosão
        public const float ExplosionRadius = 1.25f;
        public const int ExplosionDamage = 1;
        public const float ProjectileSpeed = 6f;

        // Espinho (projétil que sai ao derrotar um inimigo)
        // Slot 2 da árvore libera o espinho; o slot 3 o aprimora. "Count" = espinhos por explosão (um por inimigo derrotado).
        public const string SkillSpike = "nameSkillPrimal2";
        public const string SkillSpikeUpgrade = "nameSkillPrimal3";
        public const float Spike1Range = 8f;                    // slot 2: alcance em unidades do mundo (0 = duração original do prefab)
        public const int Spike1Pierce = 0;                      // slot 2: inimigos atravessados (0 = some no primeiro)
        public const int Spike1Count = 1;
        public const bool Spike1BreakObjects = false;
        public const float Spike2Range = 16f;                   // slot 3
        public const int Spike2Pierce = 5;
        public const int Spike2Count = 1;                       // era 3 nos testes; aumente aqui se quiser mais espinhos no slot 3
        public const bool Spike2BreakObjects = true;            // quebra objetos do cenário no caminho e segue voando
        public const string SkillSpikeMax = "nameSkillPrimal4";
        public const float Spike3Range = 20f;                   // slot 4: alcance inicial E alcance reiniciado a cada inimigo derrotado pelo espinho
        public const int Spike3Pierce = 10;
        public const int Spike3Count = 1;
        public const bool Spike3BreakObjects = true;
        public const float Spike3GrowthStep = 2.55f;               // slot 4: tamanho SOMADO a cada inimigo derrotado, como fração do tamanho inicial (1 = +100%: 1x, 2x, 3x...)
        public const float Spike3SpeedLossPerGrowth = 0.2f;    // slot 4: fração da velocidade inicial perdida a cada crescimento
        public const float Spike3MinSpeedFactor = 0.5f;         // slot 4: a velocidade nunca cai abaixo desta fração da inicial
        public const string SpikeMaxSheetFile = "ghost_spike_hungry.png"; // sprite do espinho do slot 4 (opcional; sem o PNG usa o sprite do espinho normal)
        public const int SpikeMaxFrameW = 272;                   // tamanho de CADA frame desse sprite, em pixels
        public const int SpikeMaxFrameH = 432;
        public const float SpikeMaxFps = 10f;
        public const bool SpikeMaxMatchNormalSize = true;       // true = o sprite do slot 4 tem o MESMO tamanho no mundo que o do espinho normal, mesmo com mais pixels (alta resolução)
        public const float SpikeMaxSizeMultiplier = 1f;         // multiplica o tamanho do sprite do slot 4 em relação ao normal (1 = igual)
        public const bool SpikeMirrorWhenGoingLeft = true;      // espelha o sprite (em vez de girar de cabeça para baixo) quando o espinho vai para a esquerda
        public const int Spike3MaxGrowths = 5;                  // limite de crescimentos por espinho
        public const bool Spike3ResetRangeOnKill = true;        // reinicia o alcance ao derrotar um inimigo
        public const int ProjectileDamage = 1;                  // dano em cada inimigo atingido (o original era sempre 1)
        public const float ProjectileGraceTime = 0.15f;         // ao nascer, ignora contato com objetos não sólidos (drops, efeitos do inimigo morto)
        public const bool ProjectileSpawnAtEnemy = true;        // true = nasce no centro do inimigo derrotado (mesma altura); false = no Mombo
        public const float ProjectileSpawnForwardOffset = 0.3f; // deslocamento para a frente a partir do ponto de origem
        public const float ProjectileScale = 2.5f;              // multiplica o tamanho do espinho (e a hitbox, se o collider fizer parte do prefab)
        public const string SpikeSheetFile = "ghost_spike.png"; // sprite customizado do espinho (opcional)
        public const int SpikeFrameW = 68;                      // tamanho de CADA frame do sprite do espinho, em pixels
        public const int SpikeFrameH = 104;
        public const float SpikeFps = 10f;
        public const float SpikeSpritePPUOverride = 0f;         // 0 = mesmo pixels-per-unit do sprite original do espinho

        // Explosão: o slot 5 aumenta o raio; o slot 6 faz os inimigos derrotados deixarem uma marca que explode de novo
        public const string SkillExplosion = "nameSkillPrimal5";
        public const string SkillChain = "nameSkillPrimal6";
        public const float ExplosionRadiusUpgraded = 1.5f;      // slot 5 (o raio base é ExplosionRadius)
        public const float ChainDelay = 1f;                     // slot 6: segundos entre a morte e a segunda explosão
        public const float ChainRadius = 1.1f;                  // slot 6: raio da segunda explosão (menor que a principal)
        public const int ChainDamage = 1;
        public const int ChainMaxDepth = 2;                     // 1 = só a explosão do dash deixa marcas; 2 = a secundária também deixa (cadeia de verdade)
        public const bool ChainSpawnsSpikes = false;            // as explosões secundárias também soltam espinhos?
        public const bool ChainWaitsForEnemy = true;            // a marca explode antes do prazo se um inimigo entrar na área
        public const float ChainMinTriggerDelay = 0.2f;         // tempo mínimo antes de a presença de um inimigo poder detonar a marca
        public const float ChainTriggerRadiusFactor = 1f;       // fração do raio da explosão que o inimigo precisa invadir para detonar
        public const bool DisableOriginalStompSpikes = true;    // o efeito original da skill 6 (espinhos ao pisar em inimigo) deixa de ser ativado
        public const string MarkSheetFile = "ghost_mark.png";   // marca deixada pelo inimigo (opcional; sem o PNG usa um anel procedural)
        public const int MarkFrameW = 150;
        public const int MarkFrameH = 150;
        public const float MarkFps = 8f;

        // Passiva (modo fantasma)
        public const bool LogLocalizationTerms = false;            // loga termos de tradução com "Primal"/"Dino" (para achar o nome da forma); desligue depois
        public const string PassiveSkillKey = "nameSkillPrimal7"; // skill da árvore que libera a passiva (no original: escudo ao derrotar inimigo)
        public const bool RequireSkillUnlocks = true;             // false = todas as melhorias sempre ativas (para testar sem comprar)
        public const bool DisableOriginalShield = true;           // true = o escudo original da skill 7 deixa de ser ativado
        public const float GhostDuration = 1.5f;
        public const float GhostMaxExtraTime = 1.5f;  // tempo máximo empurrando para fora; depois disso teleporta
        public const float GhostEjectSpeed = 10f;     // velocidade do empurrão para fora da parede
        public const float GhostEjectSearchRadius = 6f; // raio máximo da busca por um ponto livre
        public const float GhostFlySpeed = 4f;        // velocidade vertical com input para cima/baixo
        public const float GhostVerticalAccel = 30f;
        public const bool PhaseThroughInteractables = true; // também atravessa objetos interativos/quebráveis
        public const bool DebugLogs = false;                 // logs de diagnóstico no console do BepInEx
    }

    // =====================================================================
    //  1) HABILIDADE PRINCIPAL: DASH DIAGONAL + EXPLOSÃO
    // =====================================================================
    internal static class GhostDash
    {
        public static bool Running;
        public static bool Interrupted;   // true quando o jogo chamou StopSkill() por fora
        private static float nextUseTime;

        // Anti-travamento: o Unity mata corrotinas SEM rodar o finally quando o objeto é desativado/destruído
        // (morte, respawn, checkpoint...). Por isso a trava Running não pode depender do finally:
        // a corrotina dá um "batimento" a cada tick; se parar de bater, ela morreu e o estado é recuperado.
        private const float StaleSeconds = 0.5f;
        private static int currentRun;          // identifica a execução ativa; execuções antigas (zumbis) se encerram
        private static float lastBeat;
        private static PrimalMombo activeForm;
        private static float nextBlockedLog;

        public static void Reset()
        {
            Running = false;
            Interrupted = false;
            currentRun++;
            activeForm = null;
        }

        // Chamado a cada FixedUpdate do MomboMovement (via GhostState.Tick)
        public static void Watchdog(MomboMovement m)
        {
            if (Running && Time.time - lastBeat > StaleSeconds)
            {
                Recover(m, "watchdog");
            }
        }

        // Interrompe o dash em andamento (ex.: o Mombo levou dano no meio dele)
        public static void AbortIfRunning()
        {
            if (Running && activeForm != null) activeForm.StopSkill();
        }

        private static void Recover(MomboMovement m, string reason)
        {
            PluginMain.Log.LogWarning("[Ghost] dash travado detectado (" + reason + "): recuperando o estado");

            currentRun++;      // invalida a corrotina antiga, caso ainda exista
            Running = false;
            Interrupted = false;
            nextUseTime = Time.time + GhostConfig.DashCooldown;

            PrimalMombo f = activeForm;
            if (f != null)
            {
                try { f.StopSkill(); }
                catch (System.Exception e) { PluginMain.Log.LogWarning("[Ghost] StopSkill falhou na recuperação: " + e.Message); }
                f.isUsingSkill = false;
            }

            // Garante o estado do movimento mesmo se o StopSkill do jogo não tiver conseguido limpar
            if (m != null)
            {
                m.canApplyVelocity = true;
                m.SetUsingSkill(false);
                m.UpdateGravity(m.initialGravityScale);
                if (m.characterAnimator != null) m.characterAnimator.SetBool("Attacking", false);
            }
        }

        private static void LogBlocked()
        {
            if (!GhostConfig.DebugLogs || Time.time < nextBlockedLog) return;
            nextBlockedLog = Time.time + 1f;
            PluginMain.Log.LogInfo("[Ghost] dash recusado: Running=" + Running +
                " modoFantasmaAtivo=" + GhostState.Active +
                " cooldown=" + Mathf.Max(0f, nextUseTime - Time.time).ToString("0.00") + "s");
        }

        public static IEnumerator Run(PrimalMombo form)
        {
            MomboManager manager = MomboManager.Instance;
            if (form == null || manager == null || manager.changingScene) yield break;

            MomboMovement movement = manager.controller.momboMovement;
            if (movement == null || movement.rb == null) yield break;

            // Failsafe 1: modo fantasma que nunca terminou (Tick parou de rodar)
            if (GhostState.Active && GhostState.IsStale())
            {
                PluginMain.Log.LogWarning("[Ghost] modo fantasma preso detectado: forçando o fim");
                GhostState.ForceEnd();
                movement.UpdateGravity(movement.initialGravityScale);
            }

            // Failsafe 2: dash cuja corrotina morreu sem rodar o finally
            if (Running && Time.time - lastBeat > StaleSeconds)
            {
                Recover(movement, "pedido de dash com a corrotina anterior morta");
            }

            // Trava de reentrada: impede várias corrotinas simultâneas
            if (Running || GhostState.Active || Time.time < nextUseTime)
            {
                LogBlocked();
                yield break;
            }

            int myRun = ++currentRun;
            activeForm = form;
            lastBeat = Time.time;
            Running = true;
            Interrupted = false;
            bool impact = false;

            try
            {
                // --- preparação (espelha o MainAbility original) ---
                movement.stomping = false;
                movement.SetStomped(false);
                movement.SetUsingSkill(true);
                form.isUsingSkill = true;
                movement.canApplyVelocity = false;
                movement.UpdateGravity(0f);
                movement.characterAnimator.SetBool("Attacking", true);
                movement.characterAnimator.Play("SkillAir", 0, 0f);

                float dirX = movement.isFacingRight ? 1f : -1f;
                Vector2 velocity = new Vector2(dirX * GhostConfig.DashSpeedX, -GhostConfig.DashSpeedY);

                float elapsed = 0f;
                int tick = 0;
                int blockedTicks = 0;

                while (elapsed < GhostConfig.DashMaxTime)
                {
                    // Interrupção externa (pulo bufferizado, StopMombo...) ou troca de forma
                    if (Interrupted || myRun != currentRun || !IsCurrentForm(movement, form)) break;

                    // Cancelamento: soltou o botão da habilidade
                    if (elapsed >= GhostConfig.DashMinHoldTime && !movement.GetPressingSkill()) break;

                    movement.rb.velocity = velocity;
                    Vector2 before = movement.rb.position;

                    yield return new WaitForFixedUpdate();

                    lastBeat = Time.time;
                    if (Interrupted || myRun != currentRun) break;

                    elapsed += Time.fixedDeltaTime;
                    tick++;

                    // Impacto por deslocamento real: se um eixo andou bem menos que o esperado, bateu em algo
                    Vector2 moved = movement.rb.position - before;
                    Vector2 expected = velocity * Time.fixedDeltaTime;
                    bool blocked =
                        Mathf.Abs(moved.x) < Mathf.Abs(expected.x) * GhostConfig.BlockedRatio ||
                        Mathf.Abs(moved.y) < Mathf.Abs(expected.y) * GhostConfig.BlockedRatio;

                    blockedTicks = (tick > GhostConfig.GraceTicks && blocked) ? blockedTicks + 1 : 0;

                    if (blockedTicks >= GhostConfig.BlockedTicksNeeded ||
                        (tick > GhostConfig.GraceTicks && movement.GetGrounded()) ||
                        TouchingEnemy(movement))
                    {
                        impact = true;
                        break;
                    }
                }

                bool superseded = myRun != currentRun;

                if (impact && !Interrupted && !superseded)
                {
                    // Para NA HORA, explode e segura um instante
                    movement.rb.velocity = Vector2.zero;
                    GhostExplosion.Trigger(form, movement);

                    float lag = 0f;
                    while (lag < GhostConfig.ImpactLag && !Interrupted && myRun == currentRun)
                    {
                        movement.rb.velocity = Vector2.zero;
                        lag += Time.fixedDeltaTime;
                        yield return new WaitForFixedUpdate();
                        lastBeat = Time.time;
                    }
                }
                else if (!Interrupted && !superseded)
                {
                    // Soltou o botão ou acabou o tempo: só para
                    movement.rb.velocity = Vector2.zero;
                }
            }
            finally
            {
                // Se outra execução assumiu (recuperação do watchdog), não mexe no estado global
                if (myRun == currentRun)
                {
                    Running = false;
                    nextUseTime = Time.time + GhostConfig.DashCooldown;

                    // Se o jogo já chamou StopSkill() (Interrupted), a limpeza foi feita.
                    // Senão, chamamos nós: restaura gravidade, animator, canApplyVelocity e o pulo bufferizado.
                    if (!Interrupted && form != null && movement != null)
                    {
                        form.StopSkill();
                    }
                }
            }
        }

        private static bool IsCurrentForm(MomboMovement m, PrimalMombo form)
        {
            return m.formStateMachine != null && ReferenceEquals(m.formStateMachine.currentForm, form);
        }

        private static bool TouchingEnemy(MomboMovement m)
        {
            if (m.trigger == null) return false;
            Bounds b = m.trigger.bounds;
            Collider2D[] cols = Physics2D.OverlapBoxAll(b.center, b.size, 0f, m.enemiesLayer);
            for (int i = 0; i < cols.Length; i++)
            {
                EnemyBase e = cols[i].GetComponentInParent<EnemyBase>();
                if (e != null && e.isAlive &&
                    e.enemyType != EnemyBase.EnemyType.Projectile &&
                    e.enemyType != EnemyBase.EnemyType.Spike)
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal static class GhostExplosion
    {
        // Explosão principal do dash, no centro do corpo. O slot 5 aumenta o raio.
        public static void Trigger(PrimalMombo form, MomboMovement movement)
        {
            float radius = GhostSkills.IsUnlocked(form, GhostConfig.SkillExplosion)
                ? GhostConfig.ExplosionRadiusUpgraded
                : GhostConfig.ExplosionRadius;

            if (ShakeEffect.Instance != null) ShakeEffect.Instance.SmallShake();

            Explode(form, movement, movement.position, radius, GhostConfig.ExplosionDamage, 0);
        }

        // depth 0 = explosão do dash; 1 ou mais = explosões das marcas do slot 6
        public static void Explode(PrimalMombo form, MomboMovement movement, Vector2 center, float radius, int damage, int depth)
        {
            GhostVFX.SpawnExplosion(movement, center, radius);

            int mask = form.whatIsCollidable.value | form.objectLayer.value | movement.enemiesLayer.value;
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, mask);

            HashSet<EnemyBase> damaged = new HashSet<EnemyBase>();
            HashSet<IInteractableObject> destroyed = new HashSet<IInteractableObject>();
            List<Vector2> killPositions = new List<Vector2>(); // centro de cada inimigo derrotado
            List<EnemyBase> killedEnemies = new List<EnemyBase>(); // os espinhos atravessam estes (corpos que ainda existem)

            for (int i = 0; i < hits.Length; i++)
            {
                try
                {
                    EnemyBase enemy = hits[i].GetComponentInParent<EnemyBase>();
                    if (enemy != null && enemy.isAlive && damaged.Add(enemy))
                    {
                        Vector2 enemyCenter = hits[i].bounds.center; // antes do dano: o inimigo pode ser desativado ao morrer
                        enemy.Damage(damage);
                        if (!enemy.isAlive)
                        {
                            killPositions.Add(enemyCenter);
                            killedEnemies.Add(enemy);
                        }
                    }

                    IInteractableObject interactable = hits[i].GetComponent<IInteractableObject>();
                    if (interactable != null && destroyed.Add(interactable))
                    {
                        interactable.DestroyWithAbility();
                    }
                }
                catch (System.Exception e)
                {
                    PluginMain.Log.LogWarning("[Ghost] erro na explosão em '" + hits[i].name + "': " + e.Message);
                }
            }

            // Slot 6: cada inimigo derrotado deixa uma marca no local da morte, que explode depois de ChainDelay segundos
            if (killPositions.Count > 0 &&
                depth < GhostConfig.ChainMaxDepth &&
                GhostSkills.IsUnlocked(form, GhostConfig.SkillChain))
            {
                for (int k = 0; k < killPositions.Count; k++)
                {
                    GhostChainMark.Create(form, movement, killPositions[k], depth + 1);
                }
            }

            // Espinhos (slots 2, 3 e 4)
            bool spikesAllowed = depth == 0 || GhostConfig.ChainSpawnsSpikes;
            bool hasSpike = GhostSkills.IsUnlocked(form, GhostConfig.SkillSpike);
            if (spikesAllowed && hasSpike && killPositions.Count > 0 && form.spikeProjectilePrefab != null)
            {
                int tier = GhostSkills.IsUnlocked(form, GhostConfig.SkillSpikeMax) ? 3
                         : GhostSkills.IsUnlocked(form, GhostConfig.SkillSpikeUpgrade) ? 2
                         : 1;

                float range = tier == 3 ? GhostConfig.Spike3Range : (tier == 2 ? GhostConfig.Spike2Range : GhostConfig.Spike1Range);
                int pierce = tier == 3 ? GhostConfig.Spike3Pierce : (tier == 2 ? GhostConfig.Spike2Pierce : GhostConfig.Spike1Pierce);
                int maxCount = tier == 3 ? GhostConfig.Spike3Count : (tier == 2 ? GhostConfig.Spike2Count : GhostConfig.Spike1Count);
                bool breakObjects = tier == 3 ? GhostConfig.Spike3BreakObjects : (tier == 2 ? GhostConfig.Spike2BreakObjects : GhostConfig.Spike1BreakObjects);
                float growthStep = tier == 3 ? GhostConfig.Spike3GrowthStep : 0f;
                int maxGrowths = tier == 3 ? GhostConfig.Spike3MaxGrowths : 0;
                bool resetRange = tier == 3 && GhostConfig.Spike3ResetRangeOnKill;
                float speedLoss = tier == 3 ? GhostConfig.Spike3SpeedLossPerGrowth : 0f;
                float minSpeed = tier == 3 ? GhostConfig.Spike3MinSpeedFactor : 1f;
                bool hungrySprite = tier == 3;

                float dirX = movement.isFacingRight ? 1f : -1f;
                int count = Mathf.Min(killPositions.Count, maxCount);

                for (int k = 0; k < count; k++)
                {
                    Vector2 origin = GhostConfig.ProjectileSpawnAtEnemy ? killPositions[k] : center;
                    Vector3 spawn = new Vector3(origin.x + dirX * GhostConfig.ProjectileSpawnForwardOffset, origin.y, 0f);
                    GhostVFX.SpawnProjectile(form, movement, spawn, new Vector2(dirX * GhostConfig.ProjectileSpeed, 0f), killedEnemies,
                        range, pierce, breakObjects, growthStep, maxGrowths, resetRange, speedLoss, minSpeed, hungrySprite);
                }
            }
        }
    }

    // Slot 6: marca deixada no local onde um inimigo morreu. Pisca (mais rápido perto do fim) e depois explode.
    internal class GhostChainMark : MonoBehaviour
    {
        private PrimalMombo form;
        private MomboMovement movement;
        private SpriteRenderer sr;
        private int depth;
        private float startTime;
        private bool detonated;

        public static void Create(PrimalMombo form, MomboMovement movement, Vector2 position, int depth)
        {
            GameObject go = new GameObject("GhostChainMark");
            go.transform.position = new Vector3(position.x, position.y, movement.transform.position.z);
            go.transform.localScale = Vector3.one * (GhostConfig.ChainRadius * 2f); // mostra a área real da segunda explosão

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            GhostVFX.StyleMark(movement, renderer);

            GhostChainMark mark = go.AddComponent<GhostChainMark>();
            mark.form = form;
            mark.movement = movement;
            mark.sr = renderer;
            mark.depth = depth;
            mark.startTime = Time.time;
        }

        private void Update()
        {
            if (detonated) return;

            float elapsed = Time.time - startTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, GhostConfig.ChainDelay));
            if (sr != null)
            {
                float blink = 0.5f + 0.5f * Mathf.Sin(t * t * 40f); // pisca cada vez mais rápido
                sr.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 1f, blink));
            }

            // Fim do prazo: explode de qualquer jeito
            if (t >= 1f)
            {
                Detonate();
                return;
            }

            // Antes do fim: explode na hora se um inimigo entrar na área
            if (GhostConfig.ChainWaitsForEnemy && elapsed >= GhostConfig.ChainMinTriggerDelay && EnemyInside())
            {
                Detonate();
            }
        }

        private bool EnemyInside()
        {
            if (movement == null) return false;

            float r = GhostConfig.ChainRadius * GhostConfig.ChainTriggerRadiusFactor;
            Collider2D[] cols = Physics2D.OverlapCircleAll(transform.position, r, movement.enemiesLayer);
            for (int i = 0; i < cols.Length; i++)
            {
                EnemyBase e = cols[i].GetComponentInParent<EnemyBase>();
                if (e != null && e.isAlive &&
                    e.enemyType != EnemyBase.EnemyType.Projectile &&
                    e.enemyType != EnemyBase.EnemyType.Spike)
                {
                    return true;
                }
            }
            return false;
        }

        private void Detonate()
        {
            detonated = true;

            if (MomboManager.Instance != null && form != null && movement != null)
            {
                try
                {
                    GhostExplosion.Explode(form, movement, transform.position, GhostConfig.ChainRadius, GhostConfig.ChainDamage, depth);
                }
                catch (System.Exception e)
                {
                    PluginMain.Log.LogWarning("[Ghost] erro na explosão da marca: " + e.Message);
                }
            }

            Destroy(gameObject);
        }
    }

    // Lógica de colisão dos espinhos criados pelo mod. No original, QUALQUER contato destrói o espinho
    // (o canDamage só bloqueia o dano), o que o mataria na hora ao nascer no inimigo derrotado.
    internal class GhostSpikeMarker : MonoBehaviour
    {
        private List<EnemyBase> ignored;
        private int solidMask;
        private float graceEnd;
        private int pierceLeft;
        private int damage;
        private bool canBreak;
        private float growthStep;
        private int maxGrowths;
        private int growths;
        private float killRange;
        private float baseSpeed;
        private float speedLossPerGrowth;
        private float minSpeedFactor;
        private Vector3 baseScale;
        private Coroutine lifeRoutine;
        private readonly HashSet<IInteractableObject> brokenObjects = new HashSet<IInteractableObject>();
        private readonly HashSet<EnemyBase> alreadyHit = new HashSet<EnemyBase>();

        // growthStep <= 0: não cresce; killRange <= 0: não reinicia o alcance; speedLossPerGrowth <= 0: não fica mais lento
        public void Init(List<EnemyBase> ignoredEnemies, int solid, float graceTime, int pierce, int hitDamage, bool breakObjects,
            float growth, int maxGrowthCount, float killRangeUnits, float initialSpeed, float speedLoss, float minSpeed)
        {
            ignored = ignoredEnemies;
            solidMask = solid;
            graceEnd = Time.time + graceTime;
            pierceLeft = pierce;
            damage = hitDamage;
            canBreak = breakObjects;
            growthStep = growth;
            maxGrowths = maxGrowthCount;
            killRange = killRangeUnits;
            baseSpeed = initialSpeed;
            speedLossPerGrowth = speedLoss;
            minSpeedFactor = minSpeed;
        }

        public void HandleTrigger(Collider2D col)
        {
            EnemyBase enemy = col.GetComponentInParent<EnemyBase>();
            if (enemy != null)
            {
                if (ignored != null && ignored.Contains(enemy)) return; // inimigo derrotado pela explosão: atravessa
                if (!enemy.isAlive) return;                             // corpo que ainda existe: atravessa
                if (alreadyHit.Contains(enemy)) return;

                if (enemy.enemyType == EnemyBase.EnemyType.Enemy)
                {
                    alreadyHit.Add(enemy);
                    enemy.ObjectHit(damage);

                    if (!enemy.isAlive) OnKill(); // slot 4: cresce e reinicia o alcance

                    if (pierceLeft > 0)
                    {
                        pierceLeft--;
                        return; // segue voando
                    }
                }

                Destroy(gameObject); // acertou um inimigo (ou projétil/espinho inimigo): some, como no original
                return;
            }

            // Melhoria do slot 3: quebra objetos do cenário que estiverem no caminho e segue voando
            if (canBreak)
            {
                IInteractableObject breakable = col.gameObject.GetComponent<IInteractableObject>();
                if (breakable != null)
                {
                    if (brokenObjects.Add(breakable))
                    {
                        try { breakable.DestroyWithAbility(); }
                        catch (System.Exception e) { PluginMain.Log.LogWarning("[Ghost] erro ao quebrar '" + col.name + "': " + e.Message); }
                    }
                    return;
                }
            }

            // Não é inimigo: parede, chão ou objeto sólido destroem; efeitos e drops são ignorados só ao nascer
            bool solid = (solidMask & (1 << col.gameObject.layer)) != 0;
            if (Time.time < graceEnd && !solid) return;

            Destroy(gameObject);
        }

        // Slot 4: o espinho derrotou um inimigo
        private void OnKill()
        {
            // Crescimento ADITIVO: tamanho = inicial * (1 + crescimentos * passo). Sem explosão exponencial.
            if (growthStep > 0f && growths < maxGrowths)
            {
                if (growths == 0) baseScale = transform.localScale;
                growths++;
                transform.localScale = baseScale * (1f + growths * growthStep); // a hitbox acompanha se o collider fizer parte do prefab
            }

            // Quanto maior, mais lento (com um piso)
            float speedFactor = Mathf.Max(minSpeedFactor, 1f - growths * speedLossPerGrowth);
            SpikeBullet bullet = GetComponent<SpikeBullet>();

            if (bullet != null && baseSpeed > 0.01f && speedLossPerGrowth > 0f)
            {
                Vector2 v = bullet.rbody.velocity;
                if (v.sqrMagnitude > 0.0001f) bullet.rbody.velocity = v.normalized * (baseSpeed * speedFactor);
            }

            // Reinicia o alcance: novo prazo = distância / velocidade atual
            if (killRange > 0f && baseSpeed > 0.01f)
            {
                // Cancela o Despawn original do SpikeBullet (ele roda no componente SpikeBullet) e começa um novo prazo
                if (bullet != null) bullet.StopAllCoroutines();

                if (lifeRoutine != null) StopCoroutine(lifeRoutine);
                lifeRoutine = StartCoroutine(Lifetime(killRange / (baseSpeed * speedFactor)));
            }
        }

        private IEnumerator Lifetime(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Destroy(gameObject);
        }
    }

    // Só afeta espinhos com o marcador; os do jogo original seguem com o comportamento normal
    [HarmonyPatch(typeof(SpikeBullet), "OnTriggerEnter2D")]
    internal static class Patch_SpikeBulletTrigger
    {
        static bool Prefix(SpikeBullet __instance, Collider2D collision)
        {
            GhostSpikeMarker marker = __instance.GetComponent<GhostSpikeMarker>();
            if (marker == null) return true;

            marker.HandleTrigger(collision);
            return false; // pula o original
        }
    }

    [HarmonyPatch(typeof(PrimalMombo), "MainAbility")]
    internal static class Patch_MainAbility
    {
        static bool Prefix(PrimalMombo __instance, ref IEnumerator __result)
        {
            __result = GhostDash.Run(__instance);
            return false; // não executa o original
        }
    }

    // O jogo chama StopSkill() em buffer de pulo, StopMombo, etc. Avisamos a corrotina.
    [HarmonyPatch(typeof(PrimalMombo), "StopSkill")]
    internal static class Patch_StopSkill
    {
        static void Postfix()
        {
            GhostDash.Interrupted = true;
        }
    }

    // =====================================================================
    //  2) PASSIVA AO SOFRER DANO: MODO FANTASMA
    // =====================================================================
    internal static class GhostState
    {
        public static bool Active { get; private set; }

        private static float endTime;
        private static readonly List<KeyValuePair<int, int>> ignoredPairs = new List<KeyValuePair<int, int>>();

        private static bool wasRecoiling;
        private static float nextDebugLog;

        // Anti-prisão: última posição livre, e o ponto de saída calculado quando o efeito acaba dentro do cenário
        private static Vector2 lastSafePos;
        private static Vector2 ejectTarget;
        private static bool hasEjectTarget;
        private static bool ejected;

        // A passiva só vale se a skill correspondente da árvore foi comprada
        public static bool PassiveUnlocked(MomboMovement m)
        {
            return GhostSkills.IsUnlocked(m, GhostConfig.PassiveSkillKey);
        }

        public static void Begin(MomboMovement m, float duration, string source)
        {
            // Levou dano no meio do dash (por qualquer gatilho): o dash é interrompido, com ou sem a passiva
            GhostDash.AbortIfRunning();

            if (!PassiveUnlocked(m))
            {
                if (GhostConfig.DebugLogs)
                    PluginMain.Log.LogInfo("[Ghost] passiva bloqueada: skill " + GhostConfig.PassiveSkillKey + " não comprada");
                return;
            }

            endTime = Time.time + duration;
            if (Active) return; // já ativo: só estende o tempo

            Active = true;
            lastSafePos = m.rb.position;
            hasEjectTarget = false;
            ejected = false;

            EnablePhasing(m);
            m.UpdateGravity(0f);
            GhostVFX.StartGhostSkin(m);
            PluginMain.Log.LogInfo("[Ghost] modo fantasma ON (gatilho: " + source + ")");
        }

        private static bool IsPrimalForm(MomboMovement m)
        {
            return m.formStateMachine != null && m.formStateMachine.currentForm is PrimalMombo;
        }

        // Chamado no fim de MomboMovement.FixedUpdate (depois do CheckGrounded/MoveMombo do jogo)
        // True se o modo fantasma deveria ter acabado há tempo (o Tick parou de rodar, ex.: objeto desativado)
        public static bool IsStale()
        {
            return Active && Time.time > endTime + GhostConfig.GhostMaxExtraTime + 0.5f;
        }

        public static void Tick(MomboMovement m)
        {
            GhostDash.Watchdog(m);
            GhostTextures.ApplyToMombo(m);
            GhostPump.Pump("FixedUpdate do Mombo");
            if (GhostConfig.DebugLogs && IsPrimalForm(m)) GhostVFX.ScanSprites(m);

            // Gatilho por observação: detecta o início do knockback (isRecoiling false -> true).
            // Não depende de nenhum hook em método de dano.
            bool recoiling = m.isRecoiling;
            if (recoiling && !wasRecoiling && IsPrimalForm(m))
            {
                Begin(m, GhostConfig.GhostDuration, "isRecoiling");
            }
            wasRecoiling = recoiling;

            if (!Active) return;

            // Corpo estático (o jogo congela o Mombo, por exemplo na morte): ler ou escrever velocity só gera avisos
            // "Cannot use 'velocity' on a static body". Nada a fazer até ele voltar a ser dinâmico.
            if (m.rb != null && m.rb.bodyType == RigidbodyType2D.Static) return;

            if (GhostConfig.DebugLogs && Time.time >= nextDebugLog)
            {
                nextDebugLog = Time.time + 0.2f;
                PluginMain.Log.LogInfo("[Ghost] tick: gravityScale=" + m.rb.gravityScale +
                    " vel=" + m.rb.velocity + " recoiling=" + m.isRecoiling +
                    " canApplyVelocity=" + m.canApplyVelocity);
            }

            m.UpdateGravity(0f);

            bool inside = OverlapsSolid(m, Vector2.zero, 0.8f);
            if (!inside)
            {
                lastSafePos = m.rb.position;
                hasEjectTarget = false;
            }

            float over = Time.time - endTime;

            if (over >= 0f)
            {
                if (!inside)
                {
                    // Efeito acabou e o corpo está livre: restaura tudo
                    if (ejected) m.rb.velocity = Vector2.zero;
                    End(m);
                    return;
                }

                // Efeito acabou com o jogador DENTRO do cenário: empurra para a saída mais próxima
                if (!hasEjectTarget)
                {
                    if (!TryFindFreePosition(m, out ejectTarget)) ejectTarget = lastSafePos;
                    hasEjectTarget = true;
                    PluginMain.Log.LogInfo("[Ghost] efeito acabou dentro do cenário: empurrando para " + ejectTarget);
                }

                if (over >= GhostConfig.GhostMaxExtraTime)
                {
                    // Último recurso: teleporta para o ponto livre
                    m.rb.position = ejectTarget;
                    m.rb.velocity = Vector2.zero;
                    PluginMain.Log.LogWarning("[Ghost] tempo esgotado preso no cenário: teleportado para ponto livre");
                    End(m);
                    return;
                }

                Vector2 toTarget = ejectTarget - m.rb.position;
                float dist = toTarget.magnitude;
                if (dist > 0.01f)
                {
                    float speed = Mathf.Min(GhostConfig.GhostEjectSpeed, dist / Time.fixedDeltaTime);
                    m.rb.velocity = toTarget / dist * speed;
                    ejected = true;
                }
                return;
            }

            // Durante o Recoil o jogo controla a velocidade; depois disso o jogador flutua
            if (!m.isRecoiling && m.rb != null)
            {
                Vector2 v = m.rb.velocity;
                float targetY = m.rawMovementVec.y * GhostConfig.GhostFlySpeed;
                v.y = Mathf.MoveTowards(v.y, targetY, GhostConfig.GhostVerticalAccel * Time.fixedDeltaTime);
                m.rb.velocity = v;
            }
        }

        private static void End(MomboMovement m)
        {
            RestorePhasing();
            Active = false;
            if (m != null) m.UpdateGravity(m.initialGravityScale);
            GhostVFX.StopGhostSkin();
            PluginMain.Log.LogInfo("[Ghost] modo fantasma OFF");
        }

        public static void ForceEnd()
        {
            GhostVFX.StopGhostSkin();
            if (!Active) return;
            RestorePhasing();
            Active = false;
        }

        // Ignora a colisão entre as layers dos colliders SÓLIDOS do jogador e o cenário
        private static void EnablePhasing(MomboMovement m)
        {
            ignoredPairs.Clear();
            int solidMask = m.groundLayer.value | m.wallLayer.value;
            if (GhostConfig.PhaseThroughInteractables) solidMask |= m.whatIsInteractableObjects.value;

            HashSet<int> playerLayers = new HashSet<int>();
            Collider2D[] colliders = m.rb.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (!colliders[i].isTrigger) playerLayers.Add(colliders[i].gameObject.layer);
            }
            if (playerLayers.Count == 0) playerLayers.Add(m.rb.gameObject.layer);

            foreach (int a in playerLayers)
            {
                for (int b = 0; b < 32; b++)
                {
                    if ((solidMask & (1 << b)) == 0) continue;
                    if (Physics2D.GetIgnoreLayerCollision(a, b)) continue; // já ignorada pelo jogo: não mexe
                    Physics2D.IgnoreLayerCollision(a, b, true);
                    ignoredPairs.Add(new KeyValuePair<int, int>(a, b));
                }
            }

            LogPhasing(m, playerLayers);
        }

        // Mostra em quais layers estão o corpo do jogador e os colliders sólidos ao redor,
        // para descobrir se alguma parede está numa layer que não estamos ignorando.
        private static void LogPhasing(MomboMovement m, HashSet<int> playerLayers)
        {
            if (!GhostConfig.DebugLogs) return;

            List<string> names = new List<string>();
            foreach (int l in playerLayers) names.Add(LayerMask.LayerToName(l) + "(" + l + ")");
            PluginMain.Log.LogInfo("[Ghost] layers do corpo: " + string.Join(", ", names.ToArray()) +
                " | pares ignorados: " + ignoredPairs.Count);

            Vector2 c = m.trigger != null ? (Vector2)m.trigger.bounds.center : m.rb.position;
            Collider2D[] near = Physics2D.OverlapCircleAll(c, 2f);
            int logged = 0;
            for (int i = 0; i < near.Length && logged < 15; i++)
            {
                if (near[i].isTrigger) continue;
                int layer = near[i].gameObject.layer;
                PluginMain.Log.LogInfo("[Ghost] perto: " + near[i].name + " layer=" +
                    LayerMask.LayerToName(layer) + "(" + layer + ")");
                logged++;
            }
        }

        private static void RestorePhasing()
        {
            for (int i = 0; i < ignoredPairs.Count; i++)
            {
                Physics2D.IgnoreLayerCollision(ignoredPairs[i].Key, ignoredPairs[i].Value, false);
            }
            ignoredPairs.Clear();
        }

        // Mesma máscara usada para atravessar: se ignoramos a colisão com uma layer,
        // ela também precisa contar como "sólido" na checagem de estar preso.
        private static int SolidMask(MomboMovement m)
        {
            int mask = m.groundLayer.value | m.wallLayer.value;
            if (GhostConfig.PhaseThroughInteractables) mask |= m.whatIsInteractableObjects.value;
            return mask;
        }

        // O corpo (reduzido por 'scale') deslocado por 'offset' encosta em algum sólido?
        private static bool OverlapsSolid(MomboMovement m, Vector2 offset, float scale)
        {
            Vector2 center;
            Vector2 size;

            if (m.trigger != null)
            {
                Bounds b = m.trigger.bounds;
                center = (Vector2)b.center + offset;
                size = (Vector2)b.size * scale;
            }
            else
            {
                center = m.rb.position + offset;
                size = new Vector2(0.6f, 1f) * scale;
            }

            Collider2D[] cols = Physics2D.OverlapBoxAll(center, size, 0f, SolidMask(m));
            for (int i = 0; i < cols.Length; i++)
            {
                if (!cols[i].isTrigger) return true;
            }
            return false;
        }

        // Procura, em círculos crescentes, o ponto livre mais próximo para o corpo inteiro caber
        private static bool TryFindFreePosition(MomboMovement m, out Vector2 position)
        {
            const int directions = 16;
            for (float r = 0.25f; r <= GhostConfig.GhostEjectSearchRadius; r += 0.25f)
            {
                for (int i = 0; i < directions; i++)
                {
                    float angle = i * (2f * Mathf.PI / directions);
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    if (!OverlapsSolid(m, offset, 0.9f))
                    {
                        position = m.rb.position + offset;
                        return true;
                    }
                }
            }
            position = m.rb.position;
            return false;
        }
    }

    // =====================================================================
    //  DIAGNÓSTICO EM ARQUIVO
    //  Grava "ghost_diagnostico.txt" (na pasta da DLL) com tudo que está sendo desenhado em momentos-chave
    //  (log "Mombo Combo" do jogo, animação de morte/Combo do Mombo, textura nova de morte/Combo),
    //  para não ser preciso caçar linhas no meio do log do jogo. Cada relatório é gravado 3 vezes (agora, +0,3 s e +1 s).
    // =====================================================================
    internal static class GhostDiagnostics
    {
        private static string filePath;
        private static int dumpCount;
        private static string lastClips = "";
        private static float lastRequest = -999f;

        public static void Init(string pluginDir)
        {
            try
            {
                filePath = Path.Combine(pluginDir, "ghost_diagnostico.txt");
                File.WriteAllText(filePath, "Dark Mombo Mod | diagnóstico | build " + PluginMain.BuildTag + " | " + System.DateTime.Now + "\r\n\r\n");
                Application.logMessageReceived += OnUnityLog;
                PluginMain.Log.LogInfo("[Ghost] diagnóstico: relatórios em " + filePath);
            }
            catch (System.Exception e)
            {
                filePath = null;
                PluginMain.Log.LogWarning("[Ghost] diagnóstico: não consegui criar o arquivo: " + e.Message);
            }
        }

        public static void Shutdown()
        {
            if (filePath != null) Application.logMessageReceived -= OnUnityLog;
        }

        // O jogo escreve "Mombo Combo" no log do Unity quando o Combo acontece
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.IndexOf("Mombo Combo", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Request("log do jogo: " + condition);
            }
        }

        // Chamado a cada varredura da tela (0,25 s, tempo real): detecta animação de morte/Combo do Mombo
        public static void Poll()
        {
            if (filePath == null) return;

            Animator an = GetMomboAnimator();
            string clips = ClipNames(an);
            if (clips == lastClips) return;
            lastClips = clips;

            string low = clips.ToLowerInvariant();
            if (low.Contains("death") || low.Contains("die") || low.Contains("combo") || low.Contains("complete"))
            {
                Request("animação do Mombo mudou para: " + clips);
            }
        }

        private static readonly List<float> pendingTimes = new List<float>();
        private static readonly List<string> pendingReasons = new List<string>();

        // Grava um relatório agora e agenda outros dois (+0,3 s e +1 s), sem depender de corrotinas
        public static void Request(string reason)
        {
            if (filePath == null) return;

            float now = Time.realtimeSinceStartup;
            if (now - lastRequest < 2f) return; // evita rajadas
            lastRequest = now;

            SafeDump(reason + " (+0 s)");
            pendingTimes.Add(now + 0.3f);
            pendingReasons.Add(reason + " (+0,3 s)");
            pendingTimes.Add(now + 1f);
            pendingReasons.Add(reason + " (+1 s)");
        }

        // Chamado pelo relógio das varreduras: grava os relatórios agendados que já venceram
        public static void PumpPending(float now)
        {
            for (int i = pendingTimes.Count - 1; i >= 0; i--)
            {
                if (now < pendingTimes[i]) continue;

                string reason = pendingReasons[i];
                pendingTimes.RemoveAt(i);
                pendingReasons.RemoveAt(i);
                SafeDump(reason);
            }
        }

        // Uma linha avulsa no arquivo de diagnóstico
        public static void Note(string text)
        {
            if (filePath == null) return;

            try { File.AppendAllText(filePath, System.DateTime.Now.ToString("HH:mm:ss.fff") + " | " + text + "\r\n"); }
            catch (System.Exception) { }
        }

        private static void SafeDump(string reason)
        {
            try { Dump(reason); }
            catch (System.Exception e) { PluginMain.Log.LogWarning("[Ghost] diagnóstico: erro ao gravar: " + e.Message); }
        }

        private static void Dump(string reason)
        {
            if (filePath == null || dumpCount >= 40) return;
            dumpCount++;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== " + System.DateTime.Now.ToString("HH:mm:ss.fff") + " | " + reason);
            sb.AppendLine("timeScale=" + Time.timeScale + " | cena=" + SceneManager.GetActiveScene().name);
            sb.AppendLine("animator do Mombo: " + ClipNames(GetMomboAnimator()));

            // Só o que tem relação com o Mombo ou com algum PNG da pasta: o resto (cenário, inimigos) só enche o arquivo
            sb.AppendLine("-- SpriteRenderers ativos relacionados ao Mombo:");
            SpriteRenderer[] sprites = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
            int shown = 0;
            int omitted = 0;
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer r = sprites[i];
                if (r == null || !r.enabled || r.sprite == null || r.sprite.texture == null) continue;

                Texture2D t = r.sprite.texture;
                if (!Interesting(t)) { omitted++; continue; }
                if (shown++ < 60) AppendTexture(sb, GhostTextures.HierarchyPath(r.transform), r.sprite.name, t);
            }
            sb.AppendLine("  (" + omitted + " outros sprites omitidos)");

            sb.AppendLine("-- UI ativa relacionada ao Mombo:");
            UnityEngine.UI.Image[] images = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>();
            shown = 0;
            omitted = 0;
            for (int i = 0; i < images.Length; i++)
            {
                UnityEngine.UI.Image img = images[i];
                if (img == null || !img.enabled || img.sprite == null || img.sprite.texture == null) continue;

                Texture2D t = img.sprite.texture;
                if (!Interesting(t)) { omitted++; continue; }
                if (shown++ < 60) AppendTexture(sb, GhostTextures.HierarchyPath(img.transform), "Image", t);
            }
            sb.AppendLine("  (" + omitted + " outros elementos de UI omitidos)");

            sb.AppendLine("-- Texturas na memória que têm PNG na pasta (estado da troca):");
            Texture2D[] all = Resources.FindObjectsOfTypeAll<Texture2D>();
            shown = 0;
            for (int i = 0; i < all.Length && shown < 80; i++)
            {
                if (all[i] == null || !GhostTextures.HasPng(all[i].name)) continue;
                AppendTexture(sb, "(memória)", "", all[i]);
                shown++;
            }

            sb.AppendLine();
            File.AppendAllText(filePath, sb.ToString());
        }

        private static bool Interesting(Texture2D t)
        {
            return GhostTextures.IsRelevant(t.name) || GhostTextures.HasPng(t.name);
        }

        private static void AppendTexture(System.Text.StringBuilder sb, string owner, string spriteName, Texture2D tex)
        {
            sb.AppendLine("  " + owner + " | " + spriteName + " | textura '" + tex.name + "' " + tex.width + "x" + tex.height +
                " " + tex.format + (GhostTextures.HasPng(tex.name) ? " [tem PNG]" : "") + (GhostTextures.IsReplaced(tex) ? " [TROCADA]" : ""));
        }

        private static Animator GetMomboAnimator()
        {
            MomboManager mm = MomboManager.Instance;
            if (mm == null || mm.controller == null || mm.controller.momboMovement == null) return null;
            return mm.controller.momboMovement.characterAnimator;
        }

        private static string ClipNames(Animator an)
        {
            if (an == null || !an.isActiveAndEnabled) return "(animator inativo)";

            AnimatorClipInfo[] infos = an.GetCurrentAnimatorClipInfo(0);
            string result = "";
            for (int i = 0; i < infos.Length; i++)
            {
                result += (i > 0 ? ", " : "") + (infos[i].clip != null ? infos[i].clip.name : "?");
            }
            return result.Length == 0 ? "(nenhum clipe)" : result;
        }
    }

    // =====================================================================
    //  TROCA DE TEXTURAS EM TEMPO DE EXECUÇÃO
    //  Substitui o conteúdo de Texture2D do jogo (por nome e tamanho) por PNGs da pasta "textures".
    //  Os sprites que usam a textura passam a mostrar a arte nova, sem editar nenhum arquivo do jogo.
    //
    //  Dois tipos de arquivo:
    //   - Nome.png          substitui a textura inteira
    //   - Nome.overlay.png  é uma CAMADA: só os pixels com alpha > 0 são aplicados por cima da textura original,
    //                       então o PNG não precisa conter a arte original do jogo
    //  "Nome~2.png" (ou "Nome~2.overlay.png") serve para duas texturas com o MESMO nome e tamanhos diferentes.
    // =====================================================================
    internal static class GhostTextures
    {
        private class Replacement
        {
            public string file;
            public int width;
            public int height;
            public byte[] data;
            public bool overlay;
        }

        private static readonly Dictionary<string, List<Replacement>> byName = new Dictionary<string, List<Replacement>>();
        private static readonly HashSet<int> replacedIds = new HashSet<int>();
        private static readonly HashSet<int> mismatchLogged = new HashSet<int>();
        private static readonly HashSet<int> candidateLogged = new HashSet<int>();
        private static readonly string[] relevantWords = { "Primal", "Mombo", "SkillTree", "Easter", "Combo", "Death", "Complete" };

        public static void Init(string pluginDir)
        {
            if (!GhostConfig.ReplaceTextures) return;

            string folder = Path.Combine(pluginDir, GhostConfig.TexturesFolder);
            if (!Directory.Exists(folder))
            {
                PluginMain.Log.LogWarning("[Ghost] texturas: pasta não encontrada em " + folder + " (deve ficar ao lado da DarkMomboMod.dll)");
                return;
            }

            int count = 0;
            foreach (string path in Directory.GetFiles(folder, "*.png"))
            {
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    int w, h;
                    if (!ReadPngSize(data, out w, out h))
                    {
                        PluginMain.Log.LogWarning("[Ghost] texturas: '" + Path.GetFileName(path) + "' não é um PNG válido");
                        continue;
                    }

                    string name = Path.GetFileNameWithoutExtension(path);

                    bool overlay = false;
                    const string overlaySuffix = ".overlay";
                    if (name.EndsWith(overlaySuffix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        overlay = true;
                        name = name.Substring(0, name.Length - overlaySuffix.Length);
                    }

                    int sep = name.IndexOf('~');
                    if (sep > 0) name = name.Substring(0, sep);

                    List<Replacement> list;
                    if (!byName.TryGetValue(name, out list))
                    {
                        list = new List<Replacement>();
                        byName[name] = list;
                    }
                    list.Add(new Replacement { file = Path.GetFileName(path), width = w, height = h, data = data, overlay = overlay });
                    count++;

                    PluginMain.Log.LogInfo("[Ghost] texturas: '" + Path.GetFileName(path) + "' => textura '" + name + "' " + w + "x" + h +
                        (overlay ? " (camada)" : " (substituição total)"));
                }
                catch (System.Exception e)
                {
                    PluginMain.Log.LogWarning("[Ghost] texturas: falha ao ler " + path + ": " + e.Message);
                }
            }

            PluginMain.Log.LogInfo("[Ghost] texturas: " + count + " PNG(s) carregado(s) de " + folder);
        }

        // Texturas sem PNG correspondente (ou que falharam): não reavaliar a cada passada
        private static readonly HashSet<int> ignoredIds = new HashSet<int>();
        private static SpriteRenderer[] cachedRenderers;
        private static float nextRendererRefresh;

        // Varredura completa de todas as Texture2D na memória. Com pass >= 0, escreve um resumo no log
        // (nas primeiras passadas, a cada 20 e sempre que algo for trocado).
        public static int Apply(int pass = -1)
        {
            if (byName.Count == 0) return 0;

            Texture2D[] all = Resources.FindObjectsOfTypeAll<Texture2D>();
            int replaced = 0;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && TryApply(all[i])) replaced++;
            }

            if (GhostConfig.DebugLogs && pass >= 0 && (pass < 5 || pass % 20 == 0 || replaced > 0))
            {
                PluginMain.Log.LogInfo("[Ghost] texturas: varredura " + pass + ": " + all.Length + " Texture2D na memória, " +
                    replaced + " trocada(s) agora, " + replacedIds.Count + " no total");
            }

            return replaced;
        }

        // Troca no momento em que o jogo USA a textura: olha os sprites exibidos pelo Mombo (a cada passo de física).
        // Não depende da varredura global e pega texturas que o jogo carrega depois.
        public static void ApplyToMombo(MomboMovement m)
        {
            if (byName.Count == 0 || m == null || m.rb == null) return;

            if (cachedRenderers == null || Time.time >= nextRendererRefresh)
            {
                cachedRenderers = m.rb.GetComponentsInChildren<SpriteRenderer>(true);
                nextRendererRefresh = Time.time + 2f;
            }

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                SpriteRenderer r = cachedRenderers[i];
                if (r == null) continue;

                Sprite sp = r.sprite;
                if (sp == null) continue;

                Texture2D tex = sp.texture;
                if (tex != null) TryApply(tex);
            }
        }

        // Garante a troca da textura da árvore de habilidades assim que a tela abre
        public static void ApplyToSkillTree(SkillTreeHolder holder)
        {
            if (byName.Count == 0 || holder == null || holder.skillIcons == null) return;

            for (int i = 0; i < holder.skillIcons.Count; i++)
            {
                SkillTreeButton b = holder.skillIcons[i];
                if (b == null || b.skillIcon == null) continue;

                UnityEngine.UI.Image img = b.skillIcon.GetComponent<UnityEngine.UI.Image>();
                if (img != null && img.sprite != null && img.sprite.texture != null) TryApply(img.sprite.texture);
            }

            Apply();
        }

        // Tenta trocar uma textura. Retorna true se ela foi trocada nesta chamada.
        public static bool TryApply(Texture2D tex)
        {
            int id = tex.GetInstanceID();
            if (replacedIds.Contains(id) || ignoredIds.Contains(id)) return false;

            string texName = tex.name;

            if (GhostConfig.DebugLogs && GhostConfig.LogTextureCandidates && IsRelevant(texName) && candidateLogged.Add(id))
            {
                PluginMain.Log.LogInfo("[Ghost] texturas: em memória '" + texName + "' " + tex.width + "x" + tex.height +
                    " formato=" + tex.format + (byName.ContainsKey(texName) ? "" : " (nenhum PNG com esse nome)"));
            }

            List<Replacement> candidates;
            if (!byName.TryGetValue(texName, out candidates))
            {
                ignoredIds.Add(id);
                return false;
            }

            // Só os PNGs com o mesmo tamanho da textura: primeiro substituições totais, depois camadas
            List<Replacement> matches = new List<Replacement>();
            for (int k = 0; k < candidates.Count; k++)
            {
                if (candidates[k].width == tex.width && candidates[k].height == tex.height && !candidates[k].overlay) matches.Add(candidates[k]);
            }
            for (int k = 0; k < candidates.Count; k++)
            {
                if (candidates[k].width == tex.width && candidates[k].height == tex.height && candidates[k].overlay) matches.Add(candidates[k]);
            }

            if (matches.Count == 0)
            {
                ignoredIds.Add(id);
                if (GhostConfig.DebugLogs && mismatchLogged.Add(id))
                {
                    string available = "";
                    for (int k = 0; k < candidates.Count; k++)
                    {
                        available += (k > 0 ? ", " : "") + candidates[k].width + "x" + candidates[k].height + (candidates[k].overlay ? " (camada)" : "");
                    }

                    PluginMain.Log.LogWarning("[Ghost] texturas: '" + texName + "' tem " + tex.width + "x" + tex.height +
                        " no jogo, mas os PNGs com esse nome têm " + available + "; ignorada. Gere um PNG com " +
                        tex.width + "x" + tex.height + " (o tamanho da textura ORIGINAL do jogo).");
                }
                return false;
            }

            FilterMode filter = tex.filterMode;
            TextureWrapMode wrap = tex.wrapMode;
            bool any = false;

            for (int k = 0; k < matches.Count; k++)
            {
                bool ok = matches[k].overlay
                    ? ApplyOverlay(tex, matches[k].data)
                    : tex.LoadImage(matches[k].data, false); // precisa de UnityEngine.ImageConversionModule.dll

                if (ok)
                {
                    any = true;
                    PluginMain.Log.LogInfo("[Ghost] texturas: '" + texName + "' (" + tex.width + "x" + tex.height + ") " +
                        (matches[k].overlay ? "recebeu a camada " : "trocada por ") + matches[k].file);
                }
                else
                {
                    PluginMain.Log.LogWarning("[Ghost] texturas: falha ao aplicar " + matches[k].file + " em '" + texName + "'");
                }
            }

            if (any)
            {
                tex.filterMode = filter;
                tex.wrapMode = wrap;
                replacedIds.Add(id);
                return true;
            }

            ignoredIds.Add(id); // todas as tentativas falharam: não repetir a cada passo
            return false;
        }

        private static readonly HashSet<int> screenLogged = new HashSet<int>();
        private static float lastTimeScale = 1f;

        // Varre o que está sendo desenhado agora (SpriteRenderers e UI ativos): troca a textura de cada um e,
        // no modo diagnóstico, registra cada textura nova com o objeto que a usa. Serve para descobrir de onde vem
        // uma arte quando o nome da textura esperada nunca aparece na memória.
        // Sprites e texturas já analisados (por id de instância): cada um é visto UMA vez, e depois disso
        // a varredura custa só uma consulta barata por objeto, sem property calls nem alocação.
        private static readonly HashSet<int> seenSpriteIds = new HashSet<int>();

        public static void ScanOnScreen()
        {
            GhostDiagnostics.Poll();

            // Câmera lenta / pausa: registra para sabermos se o tempo do jogo estava alterado durante um evento
            if (GhostConfig.DebugLogs && GhostConfig.LogScreenTextures && !Mathf.Approximately(Time.timeScale, lastTimeScale))
            {
                PluginMain.Log.LogInfo("[Ghost] timeScale mudou de " + lastTimeScale + " para " + Time.timeScale);
                lastTimeScale = Time.timeScale;
            }

            SpriteRenderer[] sprites = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer r = sprites[i];
                if (r == null) continue;

                Sprite sp = r.sprite;
                if (sp == null || !seenSpriteIds.Add(sp.GetInstanceID())) continue;

                Texture2D tex = sp.texture;
                if (tex == null) continue;

                LogOnScreen(r.gameObject, tex, "SpriteRenderer");
                if (byName.Count > 0) TryApply(tex);
            }

            // UI: só Image e RawImage (os textos TextMeshPro não têm arte para trocar)
            UnityEngine.UI.Image[] images = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>();
            for (int i = 0; i < images.Length; i++)
            {
                UnityEngine.UI.Image img = images[i];
                if (img == null) continue;

                Sprite sp = img.sprite;
                if (sp == null || !seenSpriteIds.Add(sp.GetInstanceID())) continue;

                Texture2D tex = sp.texture;
                if (tex == null) continue;

                LogOnScreen(img.gameObject, tex, "Image");
                if (byName.Count > 0) TryApply(tex);
            }

            UnityEngine.UI.RawImage[] rawImages = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.RawImage>();
            for (int i = 0; i < rawImages.Length; i++)
            {
                UnityEngine.UI.RawImage raw = rawImages[i];
                if (raw == null) continue;

                Texture2D tex = raw.texture as Texture2D;
                if (tex == null || !seenSpriteIds.Add(tex.GetInstanceID())) continue;

                LogOnScreen(raw.gameObject, tex, "RawImage");
                if (byName.Count > 0) TryApply(tex);
            }
        }

        internal static bool HasPng(string textureName)
        {
            return byName.ContainsKey(textureName);
        }

        internal static bool IsReplaced(Texture2D tex)
        {
            return replacedIds.Contains(tex.GetInstanceID());
        }

        private static void LogOnScreen(GameObject owner, Texture2D tex, string kind)
        {
            if (!GhostConfig.DebugLogs) return;
            if (!screenLogged.Add(tex.GetInstanceID())) return;

            if (GhostConfig.LogScreenTextures)
            {
                PluginMain.Log.LogInfo("[Ghost] na tela: " + kind + " '" + HierarchyPath(owner.transform) + "' usa a textura '" + tex.name + "' " +
                    tex.width + "x" + tex.height + " formato=" + tex.format + (byName.ContainsKey(tex.name) ? " [tem PNG]" : ""));
            }

            // Textura nova com cara de morte/Combo: grava um relatório com tudo que está na tela agora
            string n = tex.name;
            if (n.IndexOf("Death", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Combo", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Complete", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                GhostDiagnostics.Request("textura nova na tela: " + n);
            }
        }

        internal static string HierarchyPath(Transform t)
        {
            string path = t.name;
            int depth = 0;
            while (t.parent != null && depth < 4)
            {
                t = t.parent;
                path = t.name + "/" + path;
                depth++;
            }
            return path;
        }

        // Camada: copia a textura original para uma cópia legível (via GPU, funciona com texturas não legíveis),
        // aplica por cima os pixels do PNG que têm alpha > 0 e grava o resultado de volta na textura do jogo.
        private static bool ApplyOverlay(Texture2D target, byte[] overlayPng)
        {
            Texture2D overlay = null;
            Texture2D combined = null;
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;

            try
            {
                overlay = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!overlay.LoadImage(overlayPng)) return false;

                int w = target.width;
                int h = target.height;
                if (overlay.width != w || overlay.height != h) return false;

                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(target, rt);
                RenderTexture.active = rt;

                combined = new Texture2D(w, h, TextureFormat.RGBA32, false);
                combined.ReadPixels(new Rect(0, 0, w, h), 0, 0);

                Color32[] basePx = combined.GetPixels32();
                Color32[] ovPx = overlay.GetPixels32();

                for (int i = 0; i < basePx.Length; i++)
                {
                    Color32 o = ovPx[i];
                    if (o.a == 0) continue;
                    if (o.a == 255)
                    {
                        basePx[i] = o;
                        continue;
                    }

                    // alpha parcial: composição normal ("over")
                    Color32 b = basePx[i];
                    float a = o.a / 255f;
                    float ba = b.a / 255f;
                    float outA = a + ba * (1f - a);
                    if (outA <= 0f) continue;

                    basePx[i] = new Color32(
                        (byte)Mathf.RoundToInt((o.r * a + b.r * ba * (1f - a)) / outA),
                        (byte)Mathf.RoundToInt((o.g * a + b.g * ba * (1f - a)) / outA),
                        (byte)Mathf.RoundToInt((o.b * a + b.b * ba * (1f - a)) / outA),
                        (byte)Mathf.RoundToInt(outA * 255f));
                }

                combined.SetPixels32(basePx);
                combined.Apply();

                return target.LoadImage(combined.EncodeToPNG(), false);
            }
            catch (System.Exception e)
            {
                PluginMain.Log.LogWarning("[Ghost] texturas: erro ao aplicar camada em '" + target.name + "': " + e.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (overlay != null) UnityEngine.Object.Destroy(overlay);
                if (combined != null) UnityEngine.Object.Destroy(combined);
            }
        }

        internal static bool IsRelevant(string textureName)
        {
            if (string.IsNullOrEmpty(textureName)) return false;
            for (int i = 0; i < relevantWords.Length; i++)
            {
                if (textureName.IndexOf(relevantWords[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // Lê largura e altura do cabeçalho do PNG, sem decodificar a imagem
        private static bool ReadPngSize(byte[] d, out int w, out int h)
        {
            w = 0;
            h = 0;
            if (d == null || d.Length < 24) return false;
            if (d[0] != 0x89 || d[1] != 0x50 || d[2] != 0x4E || d[3] != 0x47) return false;

            w = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
            h = (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23];
            return w > 0 && h > 0;
        }
    }

    // =====================================================================
    //  VFX CUSTOMIZADOS (carregados de PNG; sem PNG usa fallbacks procedurais)
    // =====================================================================
    internal static class GhostVFX
    {
        private static string dir;
        private static Texture2D ghostTexture;
        private static Sprite[] explosionFrames;
        private static GhostSkinBehaviour currentSkin;
        private static Texture2D spikeTexture;
        private static Sprite[] spikeFrames;      // criado no primeiro espinho e reaproveitado
        private static bool spikeInfoLogged;
        private static bool spikeStatsLogged;
        private static Sprite[] markFrames;
        private static readonly HashSet<string> seenTextures = new HashSet<string>();
        private static float nextSpriteScan;
        private static Texture2D spikeMaxTexture;
        private static Sprite[] spikeMaxFrames;

        public static void Init(string pluginDir)
        {
            dir = pluginDir;

            Texture2D markTex = LoadTexture(GhostConfig.MarkSheetFile);
            if (markTex != null)
            {
                markFrames = Slice(markTex, GhostConfig.MarkFrameW, GhostConfig.MarkFrameH, new Vector2(0.5f, 0.5f), GhostConfig.MarkFrameW);
                PluginMain.Log.LogInfo("[Ghost] VFX: marca carregada de " + GhostConfig.MarkSheetFile + " (" + markFrames.Length + " frames)");
            }
            else
            {
                markFrames = BuildProceduralMark();
                PluginMain.Log.LogInfo("[Ghost] VFX: " + GhostConfig.MarkSheetFile + " não encontrado, usando anel procedural para a marca");
            }

            spikeMaxTexture = LoadTexture(GhostConfig.SpikeMaxSheetFile);
            PluginMain.Log.LogInfo(spikeMaxTexture != null
                ? "[Ghost] VFX: sprite do espinho do slot 4 carregado de " + GhostConfig.SpikeMaxSheetFile
                : "[Ghost] VFX: " + GhostConfig.SpikeMaxSheetFile + " não encontrado, o slot 4 usa o sprite do espinho normal");

            spikeTexture = LoadTexture(GhostConfig.SpikeSheetFile);
            PluginMain.Log.LogInfo(spikeTexture != null
                ? "[Ghost] VFX: sprite do espinho carregado de " + GhostConfig.SpikeSheetFile
                : "[Ghost] VFX: " + GhostConfig.SpikeSheetFile + " não encontrado, mantendo o sprite original do espinho");

            ghostTexture = LoadTexture(GhostConfig.GhostSheetFile);
            PluginMain.Log.LogInfo(ghostTexture != null
                ? "[Ghost] VFX: skin carregada de " + GhostConfig.GhostSheetFile
                : "[Ghost] VFX: " + GhostConfig.GhostSheetFile + " não encontrado, usando tint");

            Texture2D expTex = LoadTexture(GhostConfig.ExplosionSheetFile);
            if (expTex != null)
            {
                // ppu = largura do frame => cada frame mede 1 unidade; a escala no spawn vira o diâmetro
                explosionFrames = Slice(expTex, GhostConfig.ExplosionFrameW, GhostConfig.ExplosionFrameH,
                    new Vector2(0.5f, 0.5f), GhostConfig.ExplosionFrameW);
                PluginMain.Log.LogInfo("[Ghost] VFX: explosão carregada de " + GhostConfig.ExplosionSheetFile +
                    " (" + explosionFrames.Length + " frames)");
            }
            else
            {
                explosionFrames = BuildProceduralExplosion();
                PluginMain.Log.LogInfo("[Ghost] VFX: " + GhostConfig.ExplosionSheetFile + " não encontrado, usando anel procedural");
            }
        }

        // ---------- carregamento ----------
        private static Texture2D LoadTexture(string fileName)
        {
            try
            {
                string path = Path.Combine(dir, fileName);
                if (!File.Exists(path)) return null;

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null; // precisa de UnityEngine.ImageConversionModule.dll
                tex.filterMode = FilterMode.Point; // pixel art nítida
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (System.Exception e)
            {
                PluginMain.Log.LogWarning("[Ghost] falha ao carregar " + fileName + ": " + e.Message);
                return null;
            }
        }

        // Fatia o sheet em frames: esquerda->direita, de cima para baixo
        private static Sprite[] Slice(Texture2D tex, int frameW, int frameH, Vector2 pivot, float ppu)
        {
            int cols = Mathf.Max(1, tex.width / frameW);
            int rows = Mathf.Max(1, tex.height / frameH);
            List<Sprite> list = new List<Sprite>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Rect rect = new Rect(c * frameW, tex.height - (r + 1) * frameH, frameW, frameH);
                    list.Add(Sprite.Create(tex, rect, pivot, ppu));
                }
            }
            return list.ToArray();
        }

        // Anel que se expande até a borda exata do raio de dano (usado se não houver PNG)
        private static Sprite[] BuildProceduralExplosion()
        {
            const int size = 128;
            const int frames = 8;
            Sprite[] result = new Sprite[frames];

            for (int f = 0; f < frames; f++)
            {
                float t = f / (float)(frames - 1);
                float ringR = Mathf.Lerp(0.3f, 0.93f, t);
                float thick = Mathf.Lerp(0.16f, 0.07f, t);
                float fade = 1f - 0.75f * t;

                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;

                Color[] px = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f;
                        float dy = (y + 0.5f) / size * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);

                        float ring = Mathf.Clamp01(1f - Mathf.Abs(d - ringR) / thick);
                        float fill = d < ringR ? 0.22f * (1f - d / ringR) : 0f;
                        float a = Mathf.Clamp01(ring + fill) * fade;

                        px[y * size + x] = new Color(0.55f, 0.9f, 1f, a);
                    }
                }
                tex.SetPixels(px);
                tex.Apply();
                result[f] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            }
            return result;
        }

        // ---------- explosão ----------
        public static void SpawnExplosion(MomboMovement m, Vector2 center, float radius)
        {
            if (explosionFrames == null || explosionFrames.Length == 0) return;

            GameObject go = new GameObject("GhostExplosionVFX");
            go.transform.position = new Vector3(center.x, center.y, m.transform.position.z);
            go.transform.localScale = Vector3.one * (radius * 2f); // frame de 1 unidade => diâmetro = 2 * raio

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            SpriteRenderer main = FindMainRenderer(m);
            if (main != null)
            {
                sr.sortingLayerID = main.sortingLayerID;
                sr.sortingOrder = main.sortingOrder + 10;
            }
            sr.sprite = explosionFrames[0];

            go.AddComponent<GhostOneShotAnimator>().Init(sr, explosionFrames, GhostConfig.ExplosionFps);
        }

        // ---------- espinho (projétil) ----------
        public static void SpawnProjectile(PrimalMombo form, MomboMovement movement, Vector3 position, Vector2 velocity, List<EnemyBase> ignoredEnemies,
            float range, int pierce, bool breakObjects, float growthStep, int maxGrowths, bool resetRangeOnKill,
            float speedLossPerGrowth, float minSpeedFactor, bool hungrySprite)
        {
            GameObject projectile = UnityEngine.Object.Instantiate(form.spikeProjectilePrefab, position, Quaternion.identity);

            SpikeBullet bullet = projectile.GetComponent<SpikeBullet>();

            // O jogo faz: velocidade real = velocity * spikeSpeed, e some depois de spikeDuration segundos.
            float speed = bullet != null ? velocity.magnitude * bullet.spikeSpeed : 0f;

            // Slot 4: distância que o espinho ganha a cada inimigo derrotado (o prazo é calculado pela velocidade atual)
            float killRange = (resetRangeOnKill && range > 0f) ? range : 0f;

            // Marcador: a colisão deste espinho passa a ser tratada por nós (ver Patch_SpikeBulletTrigger)
            int solidMask = movement.groundLayer.value | movement.wallLayer.value | movement.whatIsInteractableObjects.value;
            GhostSpikeMarker marker = projectile.AddComponent<GhostSpikeMarker>();
            marker.Init(ignoredEnemies, solidMask, GhostConfig.ProjectileGraceTime,
                pierce, GhostConfig.ProjectileDamage, breakObjects, growthStep, maxGrowths, killRange, speed,
                speedLossPerGrowth, minSpeedFactor);

            if (bullet != null)
            {
                if (!spikeStatsLogged)
                {
                    spikeStatsLogged = true;
                    PluginMain.Log.LogInfo("[Ghost] espinho original: spikeSpeed=" + bullet.spikeSpeed +
                        " spikeDuration=" + bullet.spikeDuration + "s => velocidade real=" + speed +
                        " un/s, alcance original=" + (speed * bullet.spikeDuration) + " un");
                }

                // Alcance independente da velocidade: duração = distância / velocidade
                if (range > 0f && speed > 0.01f)
                {
                    bullet.spikeDuration = range / speed;
                }

                bullet.StartTrajectory(velocity); // lê spikeDuration aqui, por isso o ajuste vem antes
            }

            // Escala depois do StartTrajectory, para multiplicar o que o jogo já definiu (inclui sinais de flip)
            if (!Mathf.Approximately(GhostConfig.ProjectileScale, 1f))
            {
                projectile.transform.localScale = projectile.transform.localScale * GhostConfig.ProjectileScale;
            }

            ApplySpikeSprite(projectile, hungrySprite, velocity.x < 0f && GhostConfig.SpikeMirrorWhenGoingLeft);
        }

        private static void ApplySpikeSprite(GameObject projectile, bool hungry, bool mirror)
        {
            SpriteRenderer sr = projectile.GetComponentInChildren<SpriteRenderer>();
            if (sr == null)
            {
                if (!spikeInfoLogged)
                {
                    spikeInfoLogged = true;
                    PluginMain.Log.LogWarning("[Ghost] VFX: o espinho não tem SpriteRenderer; sprite customizado ignorado");
                }
                return;
            }

            if (!spikeInfoLogged)
            {
                spikeInfoLogged = true;
                if (sr.sprite != null)
                {
                    PluginMain.Log.LogInfo("[Ghost] VFX: sprite original do espinho = " + sr.sprite.rect.width + "x" +
                        sr.sprite.rect.height + " px, ppu=" + sr.sprite.pixelsPerUnit + ", renderer=" + sr.name);
                }
            }

            // O espinho do slot 4 usa o sheet próprio, se existir; senão cai no sprite do espinho normal
            bool useMax = hungry && spikeMaxTexture != null;
            Texture2D texture = useMax ? spikeMaxTexture : spikeTexture;
            if (texture == null || sr.sprite == null) return;

            Sprite[] frames = useMax ? spikeMaxFrames : spikeFrames;
            if (frames == null)
            {
                // Casa pivot e pixels-per-unit com o sprite original (ou usa o override)
                Rect r = sr.sprite.rect;
                Vector2 pivot = new Vector2(sr.sprite.pivot.x / r.width, sr.sprite.pivot.y / r.height);
                float ppu = GhostConfig.SpikeSpritePPUOverride > 0f ? GhostConfig.SpikeSpritePPUOverride : sr.sprite.pixelsPerUnit;

                // Sprite em alta resolução: mantém o mesmo tamanho no mundo que o espinho normal.
                // Sem isso, mais pixels com o mesmo pixels-per-unit viram um sprite maior que a hitbox.
                if (useMax)
                {
                    if (GhostConfig.SpikeMaxMatchNormalSize && GhostConfig.SpikeFrameH > 0)
                    {
                        ppu *= GhostConfig.SpikeMaxFrameH / (float)GhostConfig.SpikeFrameH;
                    }
                    ppu /= Mathf.Max(0.01f, GhostConfig.SpikeMaxSizeMultiplier);

                    PluginMain.Log.LogInfo("[Ghost] VFX: sprite do slot 4 com ppu=" + ppu +
                        " => altura no mundo = " + (GhostConfig.SpikeMaxFrameH / ppu) + " un");
                }

                frames = useMax
                    ? Slice(texture, GhostConfig.SpikeMaxFrameW, GhostConfig.SpikeMaxFrameH, pivot, ppu)
                    : Slice(texture, GhostConfig.SpikeFrameW, GhostConfig.SpikeFrameH, pivot, ppu);

                if (useMax) spikeMaxFrames = frames; else spikeFrames = frames;
            }

            GhostSpriteSwapper swapper = sr.gameObject.AddComponent<GhostSpriteSwapper>();
            swapper.Init(sr, frames, useMax ? GhostConfig.SpikeMaxFps : GhostConfig.SpikeFps, mirror);
        }

        // ---------- diagnóstico: quais texturas o Mombo Primal usa ----------
        // Mostra, uma vez por textura, os sprites exibidos nos renderers do Mombo. Serve para descobrir quais
        // texturas (atlas) do jogo contêm a arte da forma, sem precisar procurar no UABEA.
        public static void ScanSprites(MomboMovement m)
        {
            if (Time.time < nextSpriteScan || m.rb == null) return;
            nextSpriteScan = Time.time + 0.5f;

            SpriteRenderer[] renderers = m.rb.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Sprite sp = renderers[i].sprite;
                if (sp == null || sp.texture == null) continue;

                Texture2D tex = sp.texture;
                if (!seenTextures.Add(tex.name)) continue;

                string rect;
                try { rect = sp.textureRect.ToString(); }
                catch (System.Exception) { rect = "(sprite empacotado em modo tight)"; }

                PluginMain.Log.LogInfo("[Ghost] sprite: renderer='" + renderers[i].name + "' sprite='" + sp.name +
                    "' textura='" + tex.name + "' " + tex.width + "x" + tex.height + " formato=" + tex.format +
                    " rect=" + rect + " ppu=" + sp.pixelsPerUnit);
            }
        }

        // ---------- marca do slot 6 ----------
        public static void StyleMark(MomboMovement m, SpriteRenderer sr)
        {
            SpriteRenderer main = FindMainRenderer(m);
            if (main != null)
            {
                sr.sortingLayerID = main.sortingLayerID;
                sr.sortingOrder = main.sortingOrder + 5;
            }

            if (markFrames == null || markFrames.Length == 0) return;

            sr.sprite = markFrames[0];
            if (markFrames.Length > 1)
            {
                sr.gameObject.AddComponent<GhostSpriteSwapper>().Init(sr, markFrames, GhostConfig.MarkFps);
            }
        }

        // Anel que mostra a área da segunda explosão, com um ponto no centro (usado se não houver PNG)
        private static Sprite[] BuildProceduralMark()
        {
            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) / 0.06f);
                    float fill = d < 0.93f ? 0.12f : 0f;
                    float dot = Mathf.Clamp01(1f - d / 0.12f) * 0.9f;

                    px[y * size + x] = new Color(0.55f, 0.9f, 1f, Mathf.Clamp01(ring + fill + dot));
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            return new Sprite[] { Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size) };
        }

        // ---------- skin do modo fantasma ----------
        public static void StartGhostSkin(MomboMovement m)
        {
            StopGhostSkin();

            SpriteRenderer main = FindMainRenderer(m);
            if (main == null)
            {
                PluginMain.Log.LogWarning("[Ghost] VFX: renderer principal do Mombo não encontrado, skin ignorada");
                return;
            }
            PluginMain.Log.LogInfo("[Ghost] VFX: renderer principal = " + main.name);

            Sprite[] frames = null;
            if (ghostTexture != null)
            {
                // Casa pivot e pixels-per-unit com o sprite original, para alinhar nos pés e manter a escala
                Vector2 pivot = new Vector2(0.5f, 0.5f);
                float ppu = 100f;
                if (main.sprite != null)
                {
                    Rect r = main.sprite.rect;
                    pivot = new Vector2(main.sprite.pivot.x / r.width, main.sprite.pivot.y / r.height);
                    ppu = main.sprite.pixelsPerUnit;
                }
                if (GhostConfig.GhostSheetPPUOverride > 0f) ppu = GhostConfig.GhostSheetPPUOverride;

                frames = Slice(ghostTexture, GhostConfig.GhostFrameW, GhostConfig.GhostFrameH, pivot, ppu);
            }

            GameObject go = new GameObject("GhostSkin");
            go.transform.SetParent(main.transform.parent, false);
            currentSkin = go.AddComponent<GhostSkinBehaviour>();
            currentSkin.Init(main, frames);
        }

        public static void StopGhostSkin()
        {
            if (currentSkin != null) currentSkin.Restore();
            currentSkin = null;
        }

        private static SpriteRenderer FindMainRenderer(MomboMovement m)
        {
            // 'spriteRenderer' é um campo privado do MomboMovement
            SpriteRenderer sr = Traverse.Create(m).Field("spriteRenderer").GetValue<SpriteRenderer>();
            if (sr == null && m.characterAnimator != null) sr = m.characterAnimator.GetComponent<SpriteRenderer>();
            if (sr == null && m.characterAnimator != null) sr = m.characterAnimator.GetComponentInChildren<SpriteRenderer>();
            if (sr == null && m.momboTransform != null) sr = m.momboTransform.GetComponentInChildren<SpriteRenderer>();
            return sr;
        }
    }

    // Mantém um sprite/sheet customizado em loop num renderer existente.
    // Roda no LateUpdate para vencer o Animator do prefab.
    internal class GhostSpriteSwapper : MonoBehaviour
    {
        private SpriteRenderer sr;
        private Sprite[] frames;
        private float fps;
        private float timer;
        private bool mirror;

        // mirror = true espelha o sprite no eixo dele (flipX). O jogo gira o espinho para apontar para onde ele voa;
        // indo para a esquerda isso o deixaria de cabeça para baixo, e o espelhamento corrige.
        public void Init(SpriteRenderer renderer, Sprite[] spriteFrames, float framesPerSecond, bool mirrorX = false)
        {
            sr = renderer;
            frames = spriteFrames;
            fps = framesPerSecond;
            mirror = mirrorX;
        }

        private void LateUpdate()
        {
            if (sr == null || frames == null || frames.Length == 0) return;

            timer += Time.deltaTime;
            sr.sprite = frames[(int)(timer * fps) % frames.Length];
            if (mirror) sr.flipX = true;
        }
    }

    // Toca os frames uma vez e se destrói
    internal class GhostOneShotAnimator : MonoBehaviour
    {
        private SpriteRenderer sr;
        private Sprite[] frames;
        private float fps;
        private float timer;

        public void Init(SpriteRenderer renderer, Sprite[] spriteFrames, float framesPerSecond)
        {
            sr = renderer;
            frames = spriteFrames;
            fps = framesPerSecond;
        }

        private void Update()
        {
            timer += Time.deltaTime;
            int i = (int)(timer * fps);
            if (i >= frames.Length)
            {
                Destroy(gameObject);
                return;
            }
            sr.sprite = frames[i];
        }
    }

    // Esconde o sprite original do Mombo e desenha o sheet fantasma por cima (ou só aplica um tint, sem PNG).
    // Roda no LateUpdate para vencer o Animator e qualquer outro script que mexa no renderer.
    internal class GhostSkinBehaviour : MonoBehaviour
    {
        private SpriteRenderer main;
        private SpriteRenderer overlay; // null no modo tint
        private Sprite[] frames;
        private Color originalColor;
        private float timer;

        public void Init(SpriteRenderer mainRenderer, Sprite[] ghostFrames)
        {
            main = mainRenderer;
            frames = ghostFrames;
            originalColor = main.color;

            if (frames != null && frames.Length > 0)
            {
                overlay = gameObject.AddComponent<SpriteRenderer>();
                overlay.sortingLayerID = main.sortingLayerID;
                overlay.sortingOrder = main.sortingOrder + 1;
                overlay.sprite = frames[0];
            }
        }

        private void LateUpdate()
        {
            if (main == null)
            {
                Destroy(gameObject);
                return;
            }

            if (overlay != null)
            {
                main.enabled = false;

                // Mesmo pai do renderer original: copia posição/escala (inclui o flip do Mombo)
                Transform t = main.transform;
                transform.localPosition = t.localPosition;
                transform.localRotation = t.localRotation;
                transform.localScale = t.localScale;
                overlay.flipX = main.flipX;
                overlay.flipY = main.flipY;

                timer += Time.deltaTime;
                overlay.sprite = frames[(int)(timer * GhostConfig.GhostSheetFps) % frames.Length];
            }
            else
            {
                main.color = GhostConfig.GhostTint;
            }
        }

        public void Restore()
        {
            if (main != null)
            {
                main.enabled = true;
                main.color = originalColor;
            }
            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++) Destroy(frames[i]);
            }
            Destroy(gameObject);
        }
    }

    // Qualquer chamada do jogo a UpdateGravity vira 0 enquanto o modo fantasma estiver ativo
    [HarmonyPatch(typeof(MomboMovement), nameof(MomboMovement.UpdateGravity))]
    internal static class Patch_UpdateGravity
    {
        static void Prefix(ref float targetGravity)
        {
            if (GhostState.Active) targetGravity = 0f;
        }
    }

    // Segunda camada: reaplica o efeito no fim de cada FixedUpdate do jogo
    [HarmonyPatch(typeof(MomboMovement), "FixedUpdate")]
    internal static class Patch_FixedUpdate
    {
        static void Postfix(MomboMovement __instance)
        {
            GhostState.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(PrimalMombo), "LooseLife")]
    internal static class Patch_LooseLife
    {
        static void Postfix(PrimalMombo __instance)
        {
            PluginMain.Log.LogInfo("[Ghost] LooseLife chamado");

            MomboManager manager = MomboManager.Instance;
            if (manager == null) return;
            MomboMovement movement = manager.controller.momboMovement;
            if (movement == null) return;

            // Levou dano no meio do dash: encerra o dash com a limpeza completa do jogo
            if (GhostDash.Running) __instance.StopSkill();

            GhostState.Begin(movement, GhostConfig.GhostDuration, "LooseLife");
        }
    }

    // =====================================================================
    //  MELHORIAS DA ÁRVORE
    // =====================================================================
    internal static class GhostSkills
    {
        // A melhoria vale se a key está em unlockedSkills da árvore do Primal (ou se RequireSkillUnlocks = false)
        public static bool IsUnlocked(PrimalMombo primal, string skillKey)
        {
            if (!GhostConfig.RequireSkillUnlocks) return true;

            return primal != null &&
                   primal.formSkillTree != null &&
                   primal.formSkillTree.unlockedSkills.ContainsKey(skillKey);
        }

        public static bool IsUnlocked(MomboMovement m, string skillKey)
        {
            PrimalMombo primal = (m != null && m.formStateMachine != null) ? m.formStateMachine.currentForm as PrimalMombo : null;
            return IsUnlocked(primal, skillKey);
        }
    }

    // Nomes e descrições mostrados na árvore. Termo -> { português, inglês }; outros idiomas usam o inglês.
    // Termos: nameSkillPrimalN (nome) e aboutSkillPrimalN (descrição). Slots ainda sem nome final aparecem como "Placeholder N".
    internal static class GhostSkillTexts
    {
        private static readonly Dictionary<string, string[]> texts = new Dictionary<string, string[]>
        {
            // Nome da forma, exibido em vários menus (termo I2 "dinoMombo", original "Mombo Dino")
            { "dinoMombo", new string[] { "Mombo Fantasma", "Ghost Mombo" } },

            { "nameSkillPrimal1", new string[] { "Investida Fantasmagórica", "Ghostly Onslaught" } },
            { "aboutSkillPrimal1", new string[] {
                "Mombo Fantasma mergulha em diagonal e explode ao bater no chão, em uma parede ou em um inimigo.",
                "Ghost Mombo dives diagonally and explodes on hitting the ground, a wall or an enemy. Release the button to cancel." } },

            { "nameSkillPrimal2", new string[] { "Espíritos Vingativos", "Vengeful Spirits" } },
            { "aboutSkillPrimal2", new string[] {
                "Inimigos derrotados pela explosão disparam um fragmento de alma para a frente.",
                "Enemies defeated by the explosion fire a soul fragment forward." } },

            { "nameSkillPrimal3", new string[] { "Alma de Aço", "Soul of Steel" } },
            { "aboutSkillPrimal3", new string[] {
                "O fragmento de alma perfura inimigos, voa mais longe e quebra objetos do cenário que estiverem no caminho.",
                "The soul fragment pierces enemies, flies farther and breaks objects in its path." } },

            { "nameSkillPrimal4", new string[] { "Faminto!", "King Boo" } },
            { "aboutSkillPrimal4", new string[] {
                "O fragmento de alma cresce a cada inimigo derrotado, mas fica mais lento, e o alcance dele recomeça a cada morte.",
                "The soul fragment grows with every enemy it defeats, but slows down, and its range restarts on each kill." } },

            { "nameSkillPrimal5", new string[] { "Explosão Espectral", "Spectral Blast" } },
            { "aboutSkillPrimal5", new string[] {
                "A explosão ganha um raio ligeiramente maior.",
                "The explosion gains a slightly larger radius." } },

            { "nameSkillPrimal6", new string[] { "BOO!-M", "BOO!-M" } },
            { "aboutSkillPrimal6", new string[] {
                "Inimigos derrotados pela explosão deixam uma marca que detona uma segunda explosão, menor, quando um inimigo se aproxima ou após 1 segundo.",
                "Enemies defeated by the explosion leave a mark that detonates a smaller second explosion when an enemy gets close, or after 1 second." } },

            { "nameSkillPrimal7", new string[] { "Último Suspiro", "Last Breath" } },
            { "aboutSkillPrimal7", new string[] {
                "Ao sofrer dano, Mombo Fantasma vira um fantasma por um instante, capaz de flutuar e atravessar paredes.",
                "After taking damage, Ghost Mombo turns into a ghost for a moment, floating and passing through walls." } },
        };

        public static bool TryGet(string term, out string text)
        {
            string[] pair;
            if (texts.TryGetValue(term, out pair))
            {
                string code = I2.Loc.LocalizationManager.CurrentLanguageCode;
                bool portuguese = code != null && code.ToLowerInvariant().StartsWith("pt");
                text = pair[portuguese ? 0 : 1];
                return true;
            }

            text = null;
            return false;
        }
    }

    // =====================================================================
    //  ÁRVORE DE HABILIDADES: diagnóstico e textos
    // =====================================================================
    // O jogo usa I2 Localization: nome e descrição das skills são "termos" (nameSkillPrimal7, aboutSkillPrimal7)
    // traduzidos por LocalizationManager.GetTermTranslation. Trocamos o resultado só para os termos da nossa passiva.
    [HarmonyPatch(typeof(I2.Loc.LocalizationManager), nameof(I2.Loc.LocalizationManager.GetTermTranslation),
        new System.Type[] { typeof(string), typeof(bool), typeof(int), typeof(bool), typeof(bool), typeof(GameObject), typeof(string), typeof(bool) })]
    internal static class Patch_I2_GetTermTranslation
    {
        private static readonly HashSet<string> loggedTerms = new HashSet<string>();

        // __0 = primeiro argumento (o termo), sem depender do nome do parâmetro no jogo
        static void Postfix(string __0, ref string __result)
        {
            if (__0 == null) return;

            if (GhostConfig.LogLocalizationTerms && __result != null &&
                (__0.Contains("Primal") || __result.Contains("Dino")) && loggedTerms.Add(__0))
            {
                PluginMain.Log.LogInfo("[Ghost] termo I2: \"" + __0 + "\" => \"" + __result.Replace("\n", "\\n") + "\"");
            }

            string text;
            if (GhostSkillTexts.TryGet(__0, out text)) __result = text;
        }
    }

    [HarmonyPatch(typeof(SkillTreeHolder), "OnEnable")]
    internal static class Patch_SkillTreeHolder_OnEnable
    {
        private static readonly HashSet<SkillTree> dumped = new HashSet<SkillTree>();

        static void Prefix(SkillTreeHolder __instance)
        {
            GhostTextures.ApplyToSkillTree(__instance); // troca a textura da árvore antes de a tela aparecer

            SkillTree tree = __instance.skillTree;
            if (tree == null || tree.formSkills == null) return;

            if (GhostConfig.DebugLogs && dumped.Add(tree)) DumpTree(__instance, tree);
        }

        private static void DumpTree(SkillTreeHolder holder, SkillTree tree)
        {
            PluginMain.Log.LogInfo("[Ghost] SkillTree '" + tree.name + "' (forma " + tree.form + "): " + tree.formSkills.Count + " skills");

            foreach (Skill s in tree.formSkills)
            {
                string price;
                try { price = ValuesManager.Instance.skillPrices[s.key.Replace("nameS", "s") + "Price"].ToString(); }
                catch (System.Exception) { price = "?"; }

                PluginMain.Log.LogInfo("[Ghost]   " + s.key + " | preço=" + price + " | custo(campo)=" + s.cost +
                    " | requer skills=[" + JoinStrings(s.requiredSkills) + "] | requer formas=[" + JoinForms(s.requiredForms) + "]" +
                    " | descrição=\"" + (s.description ?? "").Replace("\n", "\\n") + "\"");
            }

            foreach (SkillTreeButton b in holder.skillIcons)
            {
                PluginMain.Log.LogInfo("[Ghost]   botão " + b.skillKey + " -> " + (b.skillIcon != null ? b.skillIcon.name : "null"));
            }
        }

        private static string JoinStrings(string[] items)
        {
            if (items == null) return "";
            string result = "";
            for (int i = 0; i < items.Length; i++) result += (i > 0 ? ", " : "") + items[i];
            return result;
        }

        private static string JoinForms(FormName[] items)
        {
            if (items == null) return "";
            string result = "";
            for (int i = 0; i < items.Length; i++) result += (i > 0 ? ", " : "") + items[i];
            return result;
        }
    }

    // A skill 7 original dava um escudo ao derrotar inimigo (OnKillEnemy -> ActivateShield).
    // O código do jogo continua intacto: só impedimos o efeito, e a skill passa a liberar a passiva fantasma.
    [HarmonyPatch(typeof(PrimalMombo), "ActivateShield")]
    internal static class Patch_DisableOriginalShield
    {
        static bool Prefix()
        {
            return !GhostConfig.DisableOriginalShield; // false = pula o original
        }
    }

    // A skill 6 original soltava espinhos radiais ao pisar em inimigos (StompAbility -> SpawnSpikes).
    // O código do jogo continua intacto: só impedimos o efeito, e a skill passa a ser a explosão em cadeia.
    [HarmonyPatch(typeof(PrimalMombo), "SpawnSpikes")]
    internal static class Patch_DisableOriginalStompSpikes
    {
        static bool Prefix()
        {
            return !GhostConfig.DisableOriginalStompSpikes; // false = pula o original
        }
    }

    // Segundo gatilho: o knockback do jogo. Os três gatilhos (LooseLife, Recoil e a observação
    // de isRecoiling no Tick) chamam o mesmo Begin(), então não há problema em dispararem juntos.
    [HarmonyPatch(typeof(MomboMovement), nameof(MomboMovement.Recoil))]
    internal static class Patch_Recoil
    {
        static void Prefix(MomboMovement __instance)
        {
            PluginMain.Log.LogInfo("[Ghost] Recoil chamado");

            if (__instance.formStateMachine != null && __instance.formStateMachine.currentForm is PrimalMombo)
            {
                GhostState.Begin(__instance, GhostConfig.GhostDuration, "Recoil");
            }
        }
    }
}