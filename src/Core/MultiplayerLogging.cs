using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;

namespace WildWasteland.Core
{
    internal static class MultiplayerLogging
    {
        sealed class Translation
        {
            internal readonly Regex Pattern;
            internal readonly string English;
            internal Translation(string spanish, string english)
            {
                var pattern = new StringBuilder("\\A"); int start = 0;
                foreach (Match token in Regex.Matches(spanish, @"\{(\d+)\}"))
                {
                    pattern.Append(Regex.Escape(spanish.Substring(start, token.Index - start)));
                    pattern.Append("(?<p" + token.Groups[1].Value + ">.*?)");
                    english = english.Replace(token.Value, "${p" + token.Groups[1].Value + "}");
                    start = token.Index + token.Length;
                }
                pattern.Append(Regex.Escape(spanish.Substring(start))).Append("\\z");
                Pattern = new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline);
                English = english;
            }
        }
        static ManualLogSource _source;
        static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly List<Translation> Templates = new List<Translation>();
        internal static void Install()
        {
            try
            {
                _source = MultiplayerAccess.Get<ManualLogSource>(Multiplayer.Type("Plugin"), "Logger")(null);
                LoadTranslations();
                // Like Vistas' exact-source filter, this is a Core hook on the shared BepInEx sink.
                if (!Patches.Patch(null, AccessTools.Method(typeof(ManualLogSource), "Log", new[] { typeof(LogLevel), typeof(object) }),
                    prefix: AccessTools.Method(typeof(MultiplayerLogging), "Filter"))) throw new InvalidOperationException("MP logging hook failed");
            }
            catch (Exception e) { Log.Error("[Multiplayer] Logging filter could not be installed", e); }
        }
        static bool Filter(ManualLogSource __instance, LogLevel __0, ref object __1)
        {
            if (!ReferenceEquals(__instance, _source)) return true;
            if (!Log.Verbose && (__0 & ~(LogLevel.Info | LogLevel.Debug | LogLevel.Message)) == 0) return false;
            var text = __1 as string;
            if (text == null) return true;
            // The native lag diagnostic prints both languages; retain its English copy once.
            if (text.StartsWith("SPANISH:  UDP: simulando ", StringComparison.Ordinal)) return false;
            if (text.StartsWith("ENGLISH:  UDP: simulating ", StringComparison.Ordinal))
            { if (!Log.Verbose) return false; __1 = text.Substring(10); return true; }
            if (text.StartsWith("Autos compartidos:\n", StringComparison.Ordinal) || text.StartsWith("Mirando: ", StringComparison.Ordinal) || text.StartsWith("Sensores de '", StringComparison.Ordinal))
            {
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    bool cr = lines[i].EndsWith("\r", StringComparison.Ordinal);
                    lines[i] = Translate(cr ? lines[i].Substring(0, lines[i].Length - 1) : lines[i]) + (cr ? "\r" : "");
                }
                __1 = string.Join("\n", lines); return true;
            }
            __1 = Translate(text); return true;
        }
        static string Translate(string text)
        {
            string english;
            if (Exact.TryGetValue(text, out english)) return english;
            foreach (var item in Templates)
            {
                var match = item.Pattern.Match(text);
                if (match.Success) return match.Result(item.English);
            }
            return text; // Preserve unknown diagnostics from newer MP versions verbatim.
        }
        static void Add(string spanish, string english)
        {
            if (spanish.IndexOf('{') < 0) Exact.Add(spanish, english);
            else Templates.Add(new Translation(spanish, english));
        }
        static void LoadTranslations()
        {
            Exact.Clear(); Templates.Clear();
            Add("Jerarquía de escenas guardada en {0}", "Scene hierarchy saved to {0}");
            Add("FSMs guardadas en {0}", "FSMs saved to {0}");
            Add("No se pudieron volcar las FSMs: {0}", "Could not dump FSMs: {0}");
            Add("Escena cargada: '{0}' ({1})", "Scene loaded: '{0}' ({1})");
            Add("Registro: {0} jugadores conocidos en este mundo", "Player registry: {0} known players in this world");
            Add("Registro: no se pudo leer {0}: {1}", "Player registry: could not read {0}: {1}");
            Add("Registro: no se pudo escribir {0}: {1}", "Player registry: could not write {0}: {1}");
            Add("Guardado: postergado, hay alguien en un auto", "Save: postponed while a player is in a car");
            Add("Guardado: ya no hay nadie en un auto", "Save: all players have left their cars");
            Add("Guardado: empezando (autoguardado)", "Save: starting (autosave)");
            Add("Guardado: empezando (el host sale)", "Save: starting (host leaving)");
            Add("Guardado: empezando ({0})", "Save: starting ({0})");
            Add("Guardado: no se pudo escribir {0}: {1}", "Save: could not write {0}: {1}");
            Add("Guardado: listo en {0} s ({1})", "Save: completed in {0} s ({1})");
            Add("Guardado: el juego no terminó de guardar en {0} s", "Save: the game did not finish saving within {0} s");
            Add("Guardado: el host sale desde un auto; se baja primero", "Save: the host is leaving from a car; exiting the car first");
            Add("Guardado: el host no se pudo bajar del auto; no se guarda al salir (queda el último guardado)", "Save: the host could not exit the car; skipping the exit save (keeping the previous save)");
            Add("Guardado: no se pudo hacer la copia de seguridad de {0}: {1}", "Save: could not back up {0}: {1}");
            Add("Menú: no se encontraron los botones del menú principal; F-teclas siguen disponibles", "Menu: main menu buttons were not found");
            Add("Menú: botón MULTIPLAYER agregado", "Menu: MULTIPLAYER button added");
            Add("Menú: no se pudo importar {0}: {1}", "Menu: could not import {0}: {1}");
            Add("Guardado redirigido de {0} al mundo multijugador {1}", "Save redirected from {0} to multiplayer world {1}");
            Add("Variables: '{0}' sincroniza {1}", "Variables: '{0}' synchronizes {1}");
            Add("Steam: abriendo el diálogo de invitación...", "Steam: opening the invite dialog...");
            Add("Red: invitación ignorada. Salí al menú principal y aceptala de nuevo.", "Network: invitation ignored. Return to the main menu and accept it again.");
            Add("Red: no se pudo iniciar: {0}", "Network: could not start: {0}");
            Add("Red: sesión {0} iniciada como host", "Network: {0} session started as host");
            Add("Red: sesión {0} iniciada como cliente", "Network: {0} session started as client");
            Add("Red: cerrando la sesión", "Network: closing the session");
            Add("Red: se terminó la sesión: {0}", "Network: session ended: {0}");
            Add("Red: se conectó alguien, esperando su saludo", "Network: peer connected; waiting for the handshake");
            Add("Red: se desconectó {0}", "Network: {0} disconnected");
            Add("Red: mensaje {0} incompleto de {1}", "Network: incomplete message {0} from {1}");
            Add("Red: error con el mensaje {0} de {1}: {2}", "Network: error handling message {0} from {1}: {2}");
            Add("Red: {0} volvió a conectarse; se cierra su conexión anterior", "Network: {0} reconnected; closing the previous connection");
            Add("Red: rechazando a {0}: {1}", "Network: rejecting {0}: {1}");
            Add("Registro: {0} ya había jugado en este mundo; se le devuelve su estado", "Player registry: {0} has played in this world before; restoring their state");
            Add("Red: {0} entró como jugador {1}", "Network: {0} joined as player {1}");
            Add("Red: el host nos dio la bienvenida como jugador {0}", "Network: host welcomed us as player {0}");
            Add("Red: en la partida está {0} (jugador {1})", "Network: {0} is in the session (player {1})");
            Add("Red: se fue {0}", "Network: {0} left");
            Add("Red: pong #{0} de {1}, ida y vuelta {2} ms", "Network: pong #{0} from {1}, round trip {2} ms");
            Add("Red: mensaje desconocido {0} de {1}", "Network: unknown message {0} from {1}");
            Add("Red: uniéndose al mundo '{0}' (semilla {1})", "Network: joining world '{0}' (seed {1})");
            Add("Registro: estado recuperado ({0} valores, {1} objetos)", "Player registry: state restored ({0} values, {1} items)");
            Add("Enemigos: un ser vivo le pegó a {0} ({1})", "Enemies: a creature hit {0} ({1})");
            Add("Enemigos: te pegaron ({0})", "Enemies: you were hit ({0})");
            Add("Steam: creando lobby...", "Steam: creating lobby...");
            Add("Steam: entrando al lobby {0}...", "Steam: joining lobby {0}...");
            Add("Steam: lobby creado ({0})", "Steam: lobby created ({0})");
            Add("Steam: dentro del lobby de {0}", "Steam: joined {0}'s lobby");
            Add("Steam: aceptaste la invitación de {0}", "Steam: accepted the invitation from {0}");
            Add("Steam: usando el SteamManager del juego", "Steam: using the game's SteamManager");
            Add("Steam: el juego ya lo inicializó; atendemos sus avisos hasta que exista su SteamManager", "Steam: already initialized by the game; processing callbacks until its SteamManager is ready");
            Add("Steam: no está disponible (¿abriste el juego desde Steam?). Por UDP se puede jugar igual.", "Steam: unavailable (did you launch through Steam?). UDP is still available.");
            Add("Steam: lo inicializamos temprano para el menú; después el SteamManager del juego lo comparte", "Steam: initialized early for the menu; the game's SteamManager will share it");
            Add("Steam: esperando conexiones de los jugadores del lobby", "Steam: waiting for connections from lobby members");
            Add("Steam: conectando con {0}...", "Steam: connecting to {0}...");
            Add("Steam: falló el envío confiable de {0} bytes ({1})", "Steam: reliable send of {0} bytes failed ({1})");
            Add("Steam: conexión rechazada de {0} (no está en el lobby)", "Steam: rejected connection from {0} (not a lobby member)");
            Add("Steam: conectado con {0}", "Steam: connected to {0}");
            Add("Steam: se terminó la conexión con {0}: motivo {1}", "Steam: connection to {0} ended: reason {1}");
            Add("Steam: se terminó la conexión con {0}: Se terminó la sesión", "Steam: connection to {0} ended: session ended");
            Add("Steam: se terminó la conexión con {0}: No está en el lobby", "Steam: connection to {0} ended: not a lobby member");
            Add("Steam: se terminó la conexión con {0}: Falló la aceptación", "Steam: connection to {0} ended: connection acceptance failed");
            Add("Steam: se terminó la conexión con {0}: {1}", "Steam: connection to {0} ended: {1}");
            Add("UDP: esperando jugadores en el puerto {0}", "UDP: waiting for players on port {0}");
            Add("UDP: conectando a {0}...", "UDP: connecting to {0}...");
            Add("UDP: el host {0} todavía no responde ({1} intentos). ¿Apretaste F6 en la otra ventana?", "UDP: host {0} is not responding yet ({1} attempts). Has the other player started hosting?");
            Add("UDP: conexión establecida con {0}", "UDP: connection established with {0}");
            Add("Autos: pedido a quien maneja '{0}': {1} ({2})", "Cars: request to the driver of '{0}': {1} ({2})");
            Add("Autos: el otro jugador pidió {0} en '{1}'", "Cars: the other player requested {0} in '{1}'");
            Add("Autos: te subiste a '{0}', pidiendo manejarlo", "Cars: entered '{0}'; requesting driving ownership");
            Add("Autos: '{0}' seguía leyendo el teclado sin nadie al volante; apagado", "Cars: '{0}' was reading keyboard input without a driver; input disabled");
            Add("Autos: ahora manejás '{0}'", "Cars: you now control '{0}'");
            Add("Autos: {0} objetos sueltos en '{1}' o en su camino pasan a simularse acá", "Cars: {0} loose objects in or ahead of '{1}' are now simulated locally");
            Add("Autos: {0} objetos de la carga de '{1}' quedan trabados en el auto", "Cars: {0} cargo objects in '{1}' are secured to the car");
            Add("DiagAuto: '{0}' kinematic={1} vel={2} m/s motor={3} acel={4} freno={5} mano={6} marcha={7} nafta={8} otros Rigidbody adentro={9} zona={10}", "Car diagnostic: '{0}' kinematic={1} velocity={2} m/s engine={3} throttle={4} brake={5} handbrake={6} gear={7} fuel={8} other rigidbodies inside={9} zone={10}");
            Add("DiagAuto: '{0}' kinematic={1} vel={2} m/s otros Rigidbody adentro={3} zona={4}", "Car diagnostic: '{0}' kinematic={1} velocity={2} m/s other rigidbodies inside={3} zone={4}");
            Add("DiagAuto:   choca con {0} (impulso {1})", "Car diagnostic:   contact with {0} (impulse {1})");
            Add("DiagAuto:   {0} objetos del host moviéndose", "Car diagnostic:   {0} moving host objects");
            Add("DiagAuto:   '{0}' x{1} respecto del auto {2} padre={3} en zona={4} visible={5} fantasma={6} cuerpo={7} enganchada={8} tipo={9} activo={10}", "Car diagnostic:   '{0}' x{1} relative to car {2} parent={3} in zone={4} visible={5} ghost={6} rigidbody={7} attached={8} kind={9} active={10}");
            Add("Autos compartidos:", "Shared cars:");
            foreach (string owner in new[] { "host", "vos", "invitado" })
            {
                string englishOwner = owner == "vos" ? "you" : owner == "invitado" ? "guest" : "host";
                Add("  '{0}' id {1}: lo simula " + owner + ", pasivo={2} kinematic={3} vel={4} m/s motor={5} rpm%={6} marcha={7} dir={8} ruedas activas={9} piezas enganchadas={10}", "  '{0}' id {1}: simulated by " + englishOwner + ", passive={2} kinematic={3} velocity={4} m/s engine={5} rpm%={6} gear={7} steering={8} active wheels={9} attached parts={10}");
                Add("  '{0}' id {1}: lo simula " + owner + ", pasivo={2} kinematic={3} vel={4} m/s sin VehicleController ruedas activas={5} piezas enganchadas={6}", "  '{0}' id {1}: simulated by " + englishOwner + ", passive={2} kinematic={3} velocity={4} m/s no VehicleController active wheels={5} attached parts={6}");
            }
            Add("Autos: te bajaste de '{0}', vuelve al host", "Cars: exited '{0}'; ownership returns to the host");
            Add("Autos: el invitado quiso manejar '{0}', pero ya lo maneja otro", "Cars: guest requested '{0}', but another player is driving it");
            Add("Autos: el invitado maneja '{0}'", "Cars: guest is driving '{0}'");
            Add("Autos: el invitado se bajó de '{0}'", "Cars: guest exited '{0}'");
            Add("Objetos: {0} compartidos ({1} en manos del invitado), {2} actualizaciones enviadas y {3} recibidas en {4} s", "Objects: {0} shared ({1} held by guests), {2} updates sent and {3} received in {4} s");
            Add("Objetos: el objeto {0} ({1}) del jugador {2} ya no existe en el host", "Objects: object {0} ({1}) owned by player {2} no longer exists on the host");
            Add("Objetos: el jugador {0} te sacó '{1}' de la mano", "Objects: player {0} took '{1}' from your hand");
            Add("Objetos: el jugador {0} le sacó '{1}' al jugador {2}", "Objects: player {0} took '{1}' from player {2}");
            Add("Objetos: el jugador {0} agarró '{1}'", "Objects: player {0} picked up '{1}'");
            Add("Autos: sacaste '{0}' del auto", "Cars: removed '{0}' from the car");
            Add("Objetos: tenés '{0}' (en {1}), pero no es un objeto compartido: el host no lo ve", "Objects: you hold '{0}' (in {1}), but it is not shared: the host cannot see it");
            Add("Objetos: agarraste '{0}' (id {1}, en {2})", "Objects: picked up '{0}' (id {1}, in {2})");
            Add("Objetos: '{0}' quedó quieto, vuelve al host", "Objects: '{0}' settled; ownership returns to the host");
            Add("Objetos: otro jugador se quedó con '{0}'", "Objects: another player took ownership of '{0}'");
            Add("Enemigos: le pegaste a '{0}' en '{1}' ({2})", "Enemies: you hit '{0}' at '{1}' ({2})");
            Add("Enemigos: el invitado le pegó a '{0}' en '{1}' ({2})", "Enemies: guest hit '{0}' at '{1}' ({2})");
            Add("Objetos: se quitaron {0} objetos locales del piso ({1} que llevás encima quedan); ahora se usan los del host", "Objects: removed {0} local ground objects (keeping {1} carried items); using the host's objects");
            Add("Objetos: el host perdió '{0}' que llevás encima, se vuelve a compartir", "Objects: host lost your carried '{0}'; sharing it again");
            Add("Objetos: '{0}' cuelga de '{1}/{2}' pero está a {3} m; se lo trata como suelto", "Objects: '{0}' is parented to '{1}/{2}' but is {3} m away; treating it as loose");
            Add("Autos: la copia de '{0}' no tiene el hinge '{1}' (¿otro prefab?)", "Cars: the copy of '{0}' has no hinge '{1}' (different prefab?)");
            Add("Autos: '{0}' enganchada en '{1}/{2}' (tag {3}, capa {4})", "Cars: '{0}' attached to '{1}/{2}' (tag {3}, layer {4})");
            Add("Objetos: '{0}' ahora está a la vista", "Objects: '{0}' is now visible");
            Add("Objetos: '{0}' ahora está guardado en un inventario", "Objects: '{0}' is now stored in an inventory");
            Add("Autos: '{0}' se enganchó en el auto {1} ({2})", "Cars: '{0}' attached to car {1} ({2})");
            Add("Autos: '{0}' se soltó del auto {1}", "Cars: '{0}' detached from car {1}");
            Add("Objetos: '{0}' estaba {1} cm bajo nuestro terreno; lo subo", "Objects: '{0}' was {1} cm below local terrain; raising it");
            Add("Objetos: la pintura de '{0}' cambió ('{1}'); le vuelvo a poner la del host", "Objects: paint on '{0}' changed ('{1}'); restoring the host's paint");
            Add("Objetos: no se encontró el prefab '{0}'", "Objects: prefab '{0}' not found");
            Add("Objetos: hay {0} prefabs llamados '{1}'; elegí el de estructura igual al original ({2}/{3} hijos coinciden)", "Objects: found {0} prefabs named '{1}'; selected the original structure ({2}/{3} children match)");
            Add("Interruptores: {0} puertas abiertas y cosas prendidas enviadas al jugador {1}", "Switches: sent {0} open doors and enabled objects to player {1}");
            Add("Registro: {0} objetos del inventario de {1} quedan guardados en su ficha", "Player registry: stored {0} inventory items for {1}");
            Add("Registro: {0} de {1} objetos del inventario de {2} creados de nuevo", "Player registry: recreated {0} of {1} inventory items for {2}");
            Add("Registro: no existe el slot '{0}'; '{1}' queda en el piso", "Player registry: slot '{0}' does not exist; leaving '{1}' on the ground");
            Add("Registro: '{0}' vuelve a tu slot '{1}'", "Player registry: '{0}' restored to your '{1}' slot");
            Add("Objetos: el jugador {0} quiso cambiar '{1}', pero lo tiene el jugador {2}", "Objects: player {0} tried to change '{1}', but player {2} owns it");
            Add("Objetos: se gastó acá el objeto {0} ({1}), avisando al host", "Objects: consumed object {0} ({1}) locally; notifying host");
            Add("Objetos: el jugador {0} gastó '{1}', pero lo tiene el jugador {2}: se lo devuelvo", "Objects: player {0} consumed '{1}', but player {2} owns it; restoring it");
            Add("Objetos: el jugador {0} gastó '{1}'", "Objects: player {0} consumed '{1}'");
            Add("Objetos: apareció '{0}' acá, pidiéndole al host que lo comparta", "Objects: '{0}' appeared locally; requesting host registration");
            Add("Objetos: el jugador {0} creó '{1}' (id {2})", "Objects: player {0} created '{1}' (id {2})");
            Add("Explosivos: tiraste '{0}', avisando a los demás", "Explosives: you threw '{0}'; notifying peers");
            Add("Explosivos: el jugador {0} tiró '{1}'", "Explosives: player {0} threw '{1}'");
            Add("Objetos: este juego tiene los rayos configurados para ignorar triggers; lo que lleva otro jugador no se va a poder agarrar", "Objects: raycasts ignore triggers; other players' held objects cannot be picked up");
            Add("Acompañante: no se puede dibujar el resalte de la zona", "Passenger: could not draw the area highlight");
            Add("Autos: vas de acompañante en '{0}'", "Cars: you are a passenger in '{0}'");
            Add("Autos: te bajaste de '{0}'", "Cars: you exited '{0}'");
            Add("Autos: quitaron '{0}' con vos adentro; te bajaste", "Cars: '{0}' was removed while you were inside; exiting");
            Add("Autos: no hay un asiento suelto en el punto de partida; no se agrega el segundo", "Cars: no loose seat at the starting point; skipping the extra seat");
            Add("Autos: segundo asiento '{0}' agregado al punto de partida", "Cars: added extra seat '{0}' at the starting point");
            Add("Mercader: la pizarra de '{0}' tiene {1} cosas; enviada a todos", "Merchant: board for '{0}' has {1} items; sent to all peers");
            Add("Mercader: pizarra de '{0}' igualada a la del host ({1} cosas)", "Merchant: board for '{0}' matched the host ({1} items)");
            Add("Solo se puede arrancar un mundo desde el menú principal", "A world can only be started from the main menu");
            Add("No se encontró ES3SettingsMod en el menú", "ES3SettingsMod was not found in the menu");
            Add("No se encontraron el campo Seed o el botón New Game del menú", "The menu's Seed field or New Game button was not found");
            Add("Mundo nuevo con semilla {0} en {1}", "New world with seed {0} in {1}");
            Add("No se encontró el cargador de partidas del menú", "The menu's saved-game loader was not found");
            Add("Cargando mundo {0}", "Loading world {0}");
            Add("Grupo: '{0}' en '{1}' disparó con {2} jugador(es) cerca: {3}", "Group: '{0}' at '{1}' triggered with {2} nearby player(s): {3}");
            Add("Grupo: la oleada de '{0}' se cancela: nadie peleó ahí", "Group: wave at '{0}' cancelled: no combat occurred there");
            Add("Grupo: empezó la pelea en '{0}'", "Group: combat started at '{0}'");
            Add("Grupo: la oleada de '{0}' se cancela: ya no hay jugadores cerca", "Group: wave at '{0}' cancelled: no players remain nearby");
            Add("Grupo: oleada en '{0}': {1} de {2} seres vivos", "Group: wave at '{0}': {1} of {2} creatures");
            Add("Grupo: {0} seres vivos activos de {1}, {2} ms por frame, {3} jugador(es)", "Group: {0} active creatures of {1}, {2} ms per frame, {3} player(s)");
            Add("Host: el terreno ahora también se genera alrededor de los invitados", "Host: terrain now also generates around guests");
            Add("Pausa: no se encontró ScaleTime.DoTimeScale; el menú va a congelar el mundo", "Pause: ScaleTime.DoTimeScale not found; the menu will freeze the world");
            Add("Trucos: '{0}' estaba prendido; apagado para la partida multijugador", "Cheats: '{0}' was enabled; disabled for multiplayer");
            Add("Teleport a {0}: esperando que se genere el terreno...", "Teleport to {0}: waiting for terrain generation...");
            Add("Teleport listo, suelo: '{0}'", "Teleport ready, ground: '{0}'");
            Add("Teleport: el terreno no se generó a tiempo", "Teleport: terrain generation timed out");
            Add("Teleport: estabas {0} m por debajo del terreno; te subo a la superficie", "Teleport: you were {0} m below terrain; raising you to the surface");
            Add("Jugadores: no hay una capa de física libre; los jugadores se van a poder atravesar", "Players: no free physics layer; players will pass through each other");
            Add("Jugadores: capa de física {0} para que los jugadores no se atraviesen", "Players: physics layer {0} prevents players passing through each other");
            Add("Caído: quedaste caído; esperando a un compañero", "Downed: waiting for a teammate");
            Add("Caído: te rendiste", "Downed: you gave up");
            Add("Caído: nadie llegó a tiempo", "Downed: nobody arrived in time");
            Add("Caído: te levantó {0}", "Downed: revived by {0}");
            Add("Caído: levantaste a {0}", "Downed: you revived {0}");
            Add("Muerte: moriste en {0}", "Death: you died at {0}");
            Add("[Jugadores] No se encontró el efecto de disparo de {0}", "[Players] Shot effect not found for {0}");
            Add("[Jugadores] No hay modelo en tercera persona para {0}; los demás lo van a ver con la mano vacía", "[Players] No third-person model for {0}; other players will see an empty hand");
            Add("[Jugadores] Modelos de mano encontrados: {0}", "[Players] Held-item models found: {0}");
            Add("[Jugadores] No se encontró el modelo {0} o sus animaciones; se usa una cápsula.", "[Players] Model {0} or its animations were not found; using a capsule.");
            Add("[Jugadores] El jugador {0} usa el cuerpo {1}", "[Players] Player {0} uses body {1}");
            Add("[Jugadores] No se pudo medir un paso razonable de {0} ({1} m/s); se asume 4", "[Players] Could not measure a reasonable stride for {0} ({1} m/s); assuming 4");
            Add("[Jugadores] {0} está hecha para {1} m/s", "[Players] {0} is authored for {1} m/s");
            Add("Prueba: {0}", "Test: {0}");
            Add("F12: solo el host puede hacer aparecer seres vivos (el mundo lo simula él)", "F12: only the host can spawn creatures (it owns world simulation)");
            Add("F12: no hay prefabs de seres vivos cargados; andá cerca de algún lugar con enemigos", "F12: no creature prefabs loaded; move near a location with enemies");
            Add("F12: apareció '{0}' ({1}/{2})", "F12: spawned '{0}' ({1}/{2})");
            Add("F12: seres vivos cargados: {0}", "F12: loaded creatures: {0}");
            Add("Teleport cancelado: no hay suelo generado en el destino", "Teleport cancelled: no generated ground at the destination");
            Add("Teleport a {0} (suelo: '{1}')", "Teleport to {0} (ground: '{1}')");
            Add("Lugares: teleport al lado de {0} ({1}), {2} de {3}", "Places: teleport beside {0} ({1}), {2} of {3}");
            Add("Lugares: {0}", "Places: {0}");
            Add("Offset de mundo (MapMagic) {0} | NWH TotalOffset sin NWH", "World offset (MapMagic) {0} | NWH TotalOffset unavailable");
            Add("Offset de mundo (MapMagic) {0} | NWH TotalOffset {1}", "World offset (MapMagic) {0} | NWH TotalOffset {1}");
            Add("Player local {0} mundo {1}", "Player local {0} world {1}");
            Add("Mirando: {0} colliders en {1} m", "Looking: {0} colliders within {1} m");
            Add("  {0} m  '{1}' capa {2} ({3}) trigger={4}", "  {0} m  '{1}' layer {2} ({3}) trigger={4}");
            Add("  (no hay ningún item a menos de 4 m)", "  (no items within 4 m)");
            Add("Item cercano: '{0}' capa {1} activo={2} posición={3} tag={4}", "Nearby item: '{0}' layer {1} active={2} position={3} tag={4}");
            Add("  Rigidbody kinematic={0} gravedad={1}", "  Rigidbody kinematic={0} gravity={1}");
            Add("  base de colliders y={0}, techo y={1}, suelo debajo y={2}", "  collider bottom y={0}, top y={1}, ground below y={2}");
            Add("  Collider {0} en '{1}' enabled={2} trigger={3} capa {4}", "  Collider {0} on '{1}' enabled={2} trigger={3} layer {4}");
            Add("  Renderer en '{0}' enabled={1}", "  Renderer on '{0}' enabled={1}");
            Add("  FSM '{0}' enabled={1} estado='{2}'", "  FSM '{0}' enabled={1} state='{2}'");
            Add("Sensores de '{0}':", "Sensors of '{0}':");
            Add("  {0} en '{1}' (enabled={2})", "  {0} on '{1}' (enabled={2})");
            Add("Jugador: capa {0} ({1}), tag {2}", "Player: layer {0} ({1}), tag {2}");
            Add("  componentes: {0}", "  components: {0}");
            Add("Estudio: no se pudo abrir el mundo", "Study: could not open the world");
            Add("Estudio: no se pudo instanciar {0}: {1}", "Study: could not instantiate {0}: {1}");
            Add("Estudio: no se pudieron volcar las FSMs de los enemigos: {0}", "Study: could not dump enemy FSMs: {0}");
            Add("Estudio: listo", "Study: done");
        }
    }
}
