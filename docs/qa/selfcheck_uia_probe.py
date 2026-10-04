# -*- coding: utf-8 -*-
"""El instrumento externo del modo --selfcheck-uia (hito 239).

La app corre con --selfcheck-uia y un hijo EXTERNO (este fichero: python + pywinauto 0.6.9, sin
UIAccess) la observa por UI Automation con las anclas del hito 238. La app le pasa su pid en
FILEFLOW_UIA_TARGET_PID (la app vive: no la lanza la sonda). La leccion A1 del 238 aplica: ni la
ventana ni un Grid raiz sin peer materializan en el arbol UIA, asi que la app se confirma por sus
ANCLAS con peer (CanvasRoot) y sondea:

  S1. Las anclas del lienzo y la barra en el arbol por su AutomationId (CanvasRoot, ZoomLevelText,
      ZoomInButton, ZoomOutButton, FitToScreenButton).
  S2. set_focus UIA sobre CanvasRoot ENTRA (el foco del lienzo sin puntero, hallazgo del 238).
  S3. Invoke de ZoomInButton/ZoomOutButton cambia y restaura el nivel leido en ZoomLevelText.
  S4. Shift+A con el foco en el lienzo abre el spotlight y Escape lo cierra (el canal de teclado
      del lienzo abierto por el 238), restaurando el estado.
  S5. El buscador del cajon escribe por teclado UIA-inyectado ('fold', la via de la Sonda B del
      237) y el texto queda en la caja, con restauracion por backspaces.
  S6 (hito 245). El inspector con fixture montado por la app: las 5 pestañas del Pivot por su
      AutomationId (Params, Snapshots, Inputs, Outputs, Diff) — las CABECERAS viven siempre en
      el árbol. El CONTENIDO de snapshots se declara LATENTE para el canal externo: la frontera
      medida del 245 dice que materializado y EN PIE tumba al proveedor UIA del proceso (exit
      127 sin WER, con retardo de ~2-4 s, en TODA configuración: switch externo, pre-selección
      de la app, incluso solo el asentamiento sin cliente) — las tarjetas las verifica el
      selfcheck interno con su conmutación segura en proceso (try/finally desmonta).
  S7 (hito 245). La pestaña de diff con anclas de fila: el panel conmuta a Diff, la fila del
      metadato del fixture (InspectorDiffKey_Category, Added) esta en el árbol por su AutomationId
      y se vuelve a Parámetros. Sin conmutación no hay pestaña materializada en el árbol (el Pivot
      virtualiza) — y el conmutador aquí es el PATRÓN SelectionItem, no clicks.

  S0b. ANTES de todo paso que dependa de la ENTRADA: ¿hay un escritorio interactivo? En una sesión
      bloqueada, SetCursorPos + mouse_event + keybd_event caen en el backstop del escritorio de bloqueo
      y nada llega a la app. El preflight falla UNA vez con esa causa y cada veredicto posterior la
      recuerda, para que ocho pasos rojos no parezcan ocho defectos del producto.

Veredicto por codigo de salida: 0 = verificado, 2 = algun sondeo fallo, 3 = la app nunca aparecio.
"""
import ctypes
import os
import sys
import time

try:
    import comtypes
    import comtypes.client
except ImportError:  # pywinauto depende de comtypes; si falta, la via de patrón no existe
    comtypes = None

import pywinauto

from ctypes import wintypes

u32 = ctypes.windll.user32
u32.SetProcessDPIAware()
# Sin restype, un hwnd de 64 bits se trunca a c_int y «ventana en primer plano» puede leerse NULL en
# falso (o al revés): el preflight del escritorio interactivo se apoya en este valor.
u32.GetForegroundWindow.restype = wintypes.HWND


class _POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


u32.WindowFromPoint.argtypes = [_POINT]
u32.WindowFromPoint.restype = wintypes.HWND
u32.GetAncestor.argtypes = [wintypes.HWND, ctypes.c_uint]
u32.GetAncestor.restype = wintypes.HWND
u32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
u32.GetClassNameW.restype = ctypes.c_int

VK_MAP = {c: 0x41 + (ord(c) - ord("A")) for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ"}
VK_MAP.update({str(d): 0x30 + d for d in range(10)})
VK_BACK = 0x08
VK_SHIFT = 0x10

ANCHORS = ["CanvasRoot", "ZoomLevelText", "ZoomInButton", "ZoomOutButton", "FitToScreenButton"]

# Las anclas del inspector (hito 245): las 5 pestañas del Pivot y la fila de diff del metadato
# del fixture. El CONTENIDO de snapshots es LATENTE para el canal externo (la frontera medida:
# materializado y en pie tumba al proveedor) — sus tarjetas las verifica el selfcheck interno.
INSPECTOR_TABS = [
    "InspectorTabParams", "InspectorTabSnapshots", "InspectorTabInputs",
    "InspectorTabOutputs", "InspectorTabDiff",
]
INSPECTOR_DIFF_ROW = "InspectorDiffKey_Category"


def log(msg):
    print(msg, flush=True)


def key(vk, up=False):
    u32.keybd_event(vk, 0, 2 if up else 0, 0)


def type_text(text):
    for c in text:
        key(VK_MAP[c.upper()])
        time.sleep(0.03)
        key(VK_MAP[c.upper()], up=True)
        time.sleep(0.05)


def press_shift_a():
    key(VK_SHIFT)
    key(VK_MAP["A"])
    time.sleep(0.05)
    key(VK_MAP["A"], up=True)
    key(VK_SHIFT, up=True)


def press_escape():
    key(0x1B)
    time.sleep(0.05)
    key(0x1B, up=True)


def by_aid(root, aid):
    """pywinauto 0.6.9: descendants() no acepta automation_id (leccion del 237) - filtrar aqui."""
    for el in root.descendants():
        try:
            if (el.element_info.automation_id or "") == aid:
                return el
        except Exception:
            continue
    return None


def _backstop_visible():
    """¿Hay una ventana del escritorio de bloqueo tapando el escritorio? Se busca por CLASE en la
    enumeración de ventanas de nivel superior, no por un punto: el centro de la ventana de la app depende
    de dónde haya quedado al arrancar (medido: el preflight por punto daba limpio mientras el punto de
    inyección caía justo debajo del backstop)."""
    hits = []
    enum_proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    @enum_proc
    def _cb(hwnd, _lparam):
        try:
            if not u32.IsWindowVisible(hwnd):
                return True
            buf = ctypes.create_unicode_buffer(256)
            u32.GetClassNameW(hwnd, buf, 256)
            cls = buf.value or ""
            if "LockScreenBackstop" in cls or cls.startswith("LockApp"):
                hits.append(cls)
        except Exception:
            pass
        return True

    try:
        u32.EnumWindows(_cb, 0)
    except Exception:
        return None  # no poder mirar no es poder afirmar
    return hits[0] if hits else None


def interactive_desktop(win=None):
    """¿Puede la entrada sintética (teclas y clics) llegar a la ventana de la app?

    Devuelve (hay_escritorio, razon). Es la diferencia entre «el gesto no funcionó» y «no pude dar el
    gesto»: en una sesión bloqueada, `SetCursorPos` devuelve verdadero pero `mouse_event` y `keybd_event`
    caen en el BACKSTOP del escritorio de bloqueo (`LockScreenBackstopFrame`, medido sobre la propia
    ventana de la app) y NINGÚN gesto llega — ni Shift+A, ni la escritura —, incluidos los pasos que esta
    sonda lleva meses dando por buenos. Sin este preflight, ocho pasos rojos culpan a un
    producto que nunca recibió el gesto.

    Se miran DOS señales porque ninguna basta sola: el foreground puede apuntar a otra ventana mientras
    el backstop sigue tapando la app, o el foreground puede leerse NULL mientras hay ventanas «en primer
    plano» de nombre.
    """
    try:
        hwnd = u32.GetForegroundWindow()
    except Exception:
        hwnd = 0
    if not hwnd:
        return (False,
                "sin ventana en primer plano (GetForegroundWindow = NULL): la sesión está bloqueada o " +
                "no hay escritorio interactivo, y la entrada inyectada no llega a ninguna ventana")

    if win is not None:
        try:
            rect = win.rectangle()
            under = u32.WindowFromPoint(_POINT((rect.left + rect.right) // 2,
                                                (rect.top + rect.bottom) // 2))
            root = u32.GetAncestor(under, 2) or under  # GA_ROOT
            buf = ctypes.create_unicode_buffer(256)
            u32.GetClassNameW(root, buf, 256)
            cls = buf.value or ""
            if "LockScreenBackstop" in cls or cls.startswith("LockApp"):
                return (False,
                        "la sesion esta bloqueada: '%s' tapa la app y la entrada inyectada cae en el "
                        "backstop del escritorio de bloqueo" % cls)
        except Exception:
            # No poder mirar no es poder afirmar: se sigue con el resto de señales.
            pass

    backstop = _backstop_visible()
    if backstop:
        return (False,
                "la sesion esta bloqueada: '%s' tapa el escritorio y la entrada inyectada "
                "(SetCursorPos + mouse_event + keybd_event) cae en el backstop, no en la app" % backstop)

    return True, "ventana en primer plano 0x%x: la entrada puede llegar" % (hwnd & 0xFFFFFFFF)


def _input_note(detail):
    """Sufija el motivo cuando la entrada ni siquiera puede llegar a una ventana.

    Se reconsulta el foreground en el MOMENTO de escribir el veredicto, no al empezar el sondeo: la
    sesión puede bloquearse en mitad del sondeo y un detalle en rojo sin esta línea culparía a un
    producto que nunca recibió el gesto.
    """
    try:
        foreground = u32.GetForegroundWindow()
    except Exception:
        foreground = 0
    if foreground:
        return detail
    return ("%s | ENTRADA NO INYECTABLE: sin ventana en primer plano "
            "(sesion bloqueada o escritorio no interactivo)" % detail)


def select_item(el):
    """Conmuta un item de Pivot por el patrón SelectionItem de UIA.

    El Invoke sobre los headers del Pivot de WinUI no dispara la conmutación (la frontera medida
    del 231/243), pero el pattern de selección SÍ llega activo. pywinauto 0.6.9 no expone el
    wrapper: patrón por comtypes.client.GetPattern desde el elemento_info del wrapper, con
    fallback al select() del wrapper.

    FRONTERA MEDIDA (hito 245): el switch hacia una pestaña cuyo contenido lleva Expander
    (Inputs, combinada) TUMBA el proceso observado — exit 127 sin WER ni excepción gestionada;
    hacia pestañas ligeras (Diff, Parámetros) el proceso sobrevive. Por eso el único switch del
    sondeo es el de Diff (S7); la combinada la pre-selecciona la app por la vía programática y
    el observador la LEYE sin mutarla (S6).
    """
    if comtypes is not None:
        try:
            from comtypes.gen.UIAutomationClient import UIA_SelectionItemPatternId

            pattern = comtypes.client.GetPattern(el.element_info, UIA_SelectionItemPatternId)
            if pattern is not None:
                pattern.Select()
                return True
        except Exception:
            pass
    try:
        el.select()
        return True
    except Exception:
        return False


def connect_to_app(pid, probe_timeout=60.0):
    """Conecta al proceso vivo y confirma la app por sus ANCLAS (la leccion A1 del 238: ni la
    ventana ni un Grid raiz sin peer materializan en el arbol UIA; la identidad honesta es
    CanvasRoot, que SI tiene peer). Reintenta hasta el timeout."""
    from pywinauto.application import Application

    deadline = time.time() + probe_timeout
    while time.time() < deadline:
        try:
            app = Application(backend="uia").connect(process=pid, timeout=1)
            win = app.top_window()
            if by_aid(win, "CanvasRoot") is not None:
                win.set_focus()
                time.sleep(1.0)
                return win
            log("[selfcheck-uia] el arbol todavia no expone CanvasRoot; reintento")
        except Exception as ex:
            log("[selfcheck-uia] reintento de conexion: %s" % type(ex).__name__)
        time.sleep(1.0)
    return None


def probe(win):
    results = []

    # S0 (hito 245). La señal del fixture: la app escribe selfcheck-uia-fixture-ready.txt cuando
    # la escena del inspector está montada. Esperarla ANTES de cualquier sondeo: conmutar el
    # Pivot dentro de su propia reconstrucción (el primer switch de SelectionItem a los ~3 s)
    # tumba el proceso sin rastro (la muerte medida del 245: exit 127, sin WER, sin excepción
    # gestionada). Sin señal (el fixture cayó) se procede: los sondeos cantarán el FALLO
    # honesto de lo que vean.
    signal = os.environ.get("FILEFLOW_UIA_FIXTURE_SIGNAL", "")
    if signal:
        deadline = time.time() + 60.0
        while time.time() < deadline and not os.path.exists(signal):
            time.sleep(0.5)
        signal_text = ""
        if os.path.exists(signal):
            try:
                with open(signal, encoding="utf-8") as fh:
                    signal_text = (fh.read() or "").strip()
            except Exception:
                signal_text = ""
        scene_ready = signal_text.startswith("ready")
        if scene_ready:
            time.sleep(1.0)  # el asentamiento del layout tras la señal
        results.append(("S0_escena_del_fixture_lista", scene_ready,
                        signal_text if signal_text else "AUSENTE tras 60 s (el fixture no escribió)"))

    # S0b. ¿Puede la ENTRADA inyectada llegar a una ventana de verdad? Va antes que cualquier paso que
    # dependa de ella (teclas, clics) para que el informe diga la CAUSA arriba del todo: en una sesión
    # bloqueada ni SetCursorPos ni mouse_event ni keybd_event tocan la app, y sin esto ocho veredictos
    # rojos parecerían un defecto del producto. Falla el preflight, no el sondeo.
    interactive, input_reason = interactive_desktop(win)
    results.append(("S0_escritorio_interactivo", interactive, input_reason))

    # S1. Las anclas del lienzo y la barra por su AutomationId.
    found = {aid: by_aid(win, aid) for aid in ANCHORS}
    missing = [aid for aid in ANCHORS if found[aid] is None]
    results.append(("S1_anclas_en_el_arbol", not missing,
                    "5/5" if not missing else "faltan: %s" % missing))

    # S2. El foco del lienzo entra por UIA.
    focus_ok = False
    focus_detail = "sin CanvasRoot"
    if found["CanvasRoot"] is not None:
        try:
            found["CanvasRoot"].set_focus()
            time.sleep(0.8)
            focus_ok = True
            focus_detail = "set_focus ejecutado sobre CanvasRoot"
        except Exception as ex:
            focus_detail = "set_focus fallo: %s: %s" % (type(ex).__name__, ex)
    results.append(("S2_foco_del_lienzo", focus_ok, focus_detail))

    # S3. El estado del zoom, observable y restaurado.
    zoom_ok = False
    zoom_detail = "sin anclas de la barra"
    try:
        before = found["ZoomLevelText"].window_text() or ""
        found["ZoomInButton"].invoke()
        time.sleep(1.0)
        mid = found["ZoomLevelText"].window_text() or ""
        found["ZoomOutButton"].invoke()
        time.sleep(1.0)
        after = found["ZoomLevelText"].window_text() or ""
        zoom_ok = (mid != before) and (after == before)
        zoom_detail = "nivel '%s' -> '%s' -> '%s'" % (before, mid, after)
    except Exception as ex:
        zoom_detail = "invoke fallo: %s: %s" % (type(ex).__name__, ex)
    results.append(("S3_zoom_observable", zoom_ok, zoom_detail))

    # S4. El atajo del lienzo con foco UIA: Shift+A abre el spotlight, Escape lo cierra.
    spotlight_ok = False
    spotlight_detail = "sin CanvasRoot"
    try:
        found["CanvasRoot"].set_focus()
        time.sleep(0.5)
        was_open = by_aid(win, "SpotlightSearchBox") is not None
        press_shift_a()
        time.sleep(1.5)
        now_open = by_aid(win, "SpotlightSearchBox") is not None
        opened = now_open and not was_open
        if opened:
            press_escape()
            time.sleep(1.0)
            closed = by_aid(win, "SpotlightSearchBox") is None
        else:
            closed = False
        spotlight_ok = opened and closed
        spotlight_detail = ("spotlight abierto y cerrado por Escape"
                            if spotlight_ok else
                            "abierto=%s cerrado=%s" % (opened, closed))
    except Exception as ex:
        spotlight_detail = "atajo fallo: %s: %s" % (type(ex).__name__, ex)
    results.append(("S4_atajo_del_lienzo", spotlight_ok,
                    _input_note(spotlight_detail)))

    # S5. El buscador del cajon escribe por teclado UIA-inyectado, con restauracion.
    search_ok = False
    search_detail = "sin SearchBox"
    try:
        search = by_aid(win, "SearchBox")
        if search is not None:
            search.set_focus()
            time.sleep(0.5)
            type_text("fold")
            time.sleep(1.5)
            typed = (search.window_text() or "")
            for _ in range(4):
                key(VK_BACK)
                time.sleep(0.03)
                key(VK_BACK, up=True)
                time.sleep(0.05)
            time.sleep(1.0)
            restored = (search.window_text() or "")
            search_ok = "fold" in typed.lower() and restored.strip() == ""
            search_detail = "texto '%s' (restaurado '%s')" % (typed, restored)
        else:
            search_detail = "el buscador del cajon no esta en el arbol"
    except Exception as ex:
        search_detail = "buscador fallo: %s: %s" % (type(ex).__name__, ex)
    results.append(("S5_buscador_ui_inyectado", search_ok,
                    _input_note(search_detail)))

    # S6 (hito 245). Las CABECERAS de las 5 pestañas del inspector en el árbol (siempre
    # materializadas, sin conmutar ni pre-seleccionar): la estructura del Pivot observable.
    # El CONTENIDO de snapshots queda LATENTE para el canal externo — la frontera medida del
    # 245 (materializado y en pie tumba al proveedor UIA del proceso, con retardo, en toda
    # configuración) — y sus tarjetas las verifica el selfcheck interno con la conmutación
    # segura en proceso que desmonta al restaurar.
    tabs_ok = False
    tabs_detail = "sin pestañas del inspector"
    try:
        found_tabs = {aid: by_aid(win, aid) for aid in INSPECTOR_TABS}
        missing_tabs = [aid for aid in INSPECTOR_TABS if found_tabs[aid] is None]
        tabs_ok = not missing_tabs
        tabs_detail = ("5 pestañas del Pivot en el árbol por su AID (Parámetros | Snapshots | "
                       "Entradas | Salidas | Diff); contenido de snapshots LATENTE para el canal "
                       "externo (frontera medida del 245) — tarjetas verificadas por el selfcheck "
                       "interno"
                       if tabs_ok else
                       "faltan pestañas: %s" % missing_tabs)
    except Exception as ex:
        tabs_detail = "pestañas fallo: %s: %s" % (type(ex).__name__, ex)
    results.append(("S6_pestañas_del_inspector", tabs_ok, tabs_detail))

    # S7 (hito 245). La pestaña de diff con anclas de fila: el panel conmuta a Diff, la fila del
    # metadato del fixture (InspectorDiffKey_Category, Added) queda en el árbol por su AutomationId
    # y se vuelve a Parámetros. Sin conmutación no hay pestaña materializada (el Pivot virtualiza)
    # — y el conmutador aquí es el PATRÓN SelectionItem, no clicks. Es el ÚNICO switch del sondeo:
    # la pestaña Diff es ligera (sin Expander) y sobrevive a la frontera medida del 245.
    diff_ok = False
    diff_detail = "sin pestaña de diff"
    try:
        diff_tab = by_aid(win, "InspectorTabDiff")
        params_tab = by_aid(win, "InspectorTabParams")
        if diff_tab is None or params_tab is None:
            diff_detail = "faltan las anclas de pestaña (Diff=%s, Params=%s)" % (
                diff_tab is not None, params_tab is not None)
        else:
            switch_ok = select_item(diff_tab)
            time.sleep(1.5)
            row = by_aid(win, INSPECTOR_DIFF_ROW)
            row_text = (row.window_text() or "") if row is not None else ""
            back_ok = select_item(params_tab)
            time.sleep(1.5)
            diff_ok = switch_ok and row is not None and "Category" in row_text
            diff_detail = ("fila '%s' en el árbol con el texto '%s' (Added del fixture)"
                           % (INSPECTOR_DIFF_ROW, row_text.strip()) if diff_ok
                           else "fila=%s texto='%s'" % (row is not None, row_text))
    except Exception as ex:
        diff_detail = "diff fallo: %s: %s" % (type(ex).__name__, ex)
    results.append(("S7_diff_con_ancla_de_fila", diff_ok, diff_detail))

    return results


def main():
    pid_env = os.environ.get("FILEFLOW_UIA_TARGET_PID", "")
    if not pid_env.isdigit():
        log("[selfcheck-uia] FALLO: sin FILEFLOW_UIA_TARGET_PID (el modo lo pasa la app)")
        return 3
    pid = int(pid_env)

    win = connect_to_app(pid)
    if win is None:
        log("[selfcheck-uia] FALLO: la app no aparecio por UIA en el tiempo previsto")
        return 3

    log("[selfcheck-uia] ventana ancla encontrada (pid %s)" % pid)
    results = probe(win)

    passed = 0
    log("[selfcheck-uia] RESULTADOS:")
    for name, ok, detail in results:
        passed += 1 if ok else 0
        log("[selfcheck-uia]   %s  %s: %s" % ("[OK]" if ok else "[FALLO]", name, detail))
    verdict = "VERIFICADO" if passed == len(results) else "FALLO (%d/%d)" % (passed, len(results))
    log("[selfcheck-uia] RESULTADO: %s" % verdict)
    return 0 if passed == len(results) else 2


if __name__ == "__main__":
    sys.exit(main())
