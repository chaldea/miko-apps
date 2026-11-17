"""Read-only MCP inspection plus device previews. Run with the FBox simulator open."""
import base64
import json
import pathlib
import urllib.request

OUTPUT = pathlib.Path("FBox/artifacts/mcp")
OUTPUT.mkdir(parents=True, exist_ok=True)


def rpc(method, params):
    request = urllib.request.Request(
        "http://localhost:5801",
        data=json.dumps({"jsonrpc": "2.0", "id": 1, "method": method, "params": params}).encode(),
        headers={"Content-Type": "application/json", "Accept": "application/json, text/event-stream"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read().decode()
    if body.lstrip().startswith("{"):
        return json.loads(body)
    return json.loads(next(line[6:] for line in body.splitlines() if line.startswith("data: ")))


def call(name, arguments=None, label=None):
    result = rpc("tools/call", {"name": name, "arguments": arguments or {}})
    (OUTPUT / f"{label or name}.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    content = result.get("result", {}).get("content", [])
    value = "\n".join(c["text"] for c in content if c["type"] == "text")
    if result.get("result", {}).get("isError"):
        print(f"UNAVAILABLE: {name}: {value}")
        return None
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        return value


def screenshot(label):
    data = call("simulator_screenshot", label=label)
    if isinstance(data, str) and data.startswith("data:image/png;base64,"):
        (OUTPUT / f"{label}.png").write_bytes(base64.b64decode(data.split(",", 1)[1]))
    else:
        raise RuntimeError("Simulator did not return a PNG screenshot")


rpc("initialize", {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "fbox-verification", "version": "1"}})
original = call("simulator_get_current_device")
try:
    rows = call("devtools_query_elements", {"selector": ".device-row"})
    assert rows, "Expected visible device rows"
    box = call("devtools_get_box_model", {"elementId": rows[0]["id"]})
    assert box, "Expected a box model"
    call("devtools_get_computed_style", {"elementId": rows[0]["id"]})
    screenshot("simulator")
    call("simulator_select_device", {"deviceName": "iPhone SE"})
    call("simulator_toggle_safe_area", {"enabled": True})
    screenshot("iphone-se")
    call("simulator_set_orientation", {"orientation": "Landscape"})
    screenshot("iphone-se-landscape")
    call("devtools_get_logs", {"maxCount": 50})
finally:
    call("simulator_set_orientation", {"orientation": "Portrait"}, label="restore-orientation")
    call("simulator_select_device", {"deviceName": original["name"]}, label="restore-device")
print(f"Inspected {len(rows)} device rows, box model, logs and three screenshots. Output: {OUTPUT}")
