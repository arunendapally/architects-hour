# SPEC — D4: Your first tool-calling agent

*Planned 03 Sep 2026.*

## What it is

Day 4 opens the **tools** box: give the model one function it can call and let it decide
when to use it. The tool is `read_file(path)` — a real side effect (it reads from the
local filesystem). The agentic loop that makes tool calling work is built by hand: send
`messages` + a `tools` array, the model replies with `tool_calls`, we execute each one,
append the results as `tool`-role messages, and loop until the model answers with text.

## Why manual

Tool calling is the primitive every agent is built from. The loop is small and explicit:
advertise a schema, run the requested function, feed the result back. Doing it once by
hand makes frameworks obvious — they only hide this same loop.

## What's new vs D1-D3

D1's `IModelClient` is single-shot (system + user → one answer) and can't send a `tools`
array or continue after a tool result. So the tool loop is genuinely new wire code, not a
reuse. The project is self-contained; the chat provider (NVIDIA NIM, OpenAI-compatible)
supports the `tools` field.

| File | What it does |
|---|---|
| `ToolAgentClient.cs` | The loop: POST messages+tools, parse `tool_calls`, append tool results, repeat |
| `Tools.cs` | The one tool: `read_file` schema + dispatcher |
| `Program.cs` | CLI, system prompt, runs the agent, prints the answer |
| `data/note.txt` | A bundled file the model can be asked about |

## Provider

NVIDIA NIM chat (`nvidia/nemotron-3.5-lightning-30b-a3b`), which supports tool calling.
Key `NVIDIA_API_KEY`, shared secret store with D1-D3. Thinking enabled via
`chat_template_kwargs` + `reasoning_budget`, same as D1.

## Agent loop (the core)

```
messages = [system, user]
repeat (max 8 turns):
    reply = POST /chat/completions {model, messages, tools, ...}
    if reply has no tool_calls:
        print reply.content; stop
    append reply (assistant tool_calls) to messages
    for each tool_call:
        result = execute(tool_call)
        append {role:tool, tool_call_id, content:result} to messages
error out if the loop runs out of turns without a text answer
```

## CLI

```
--prompt "..."     or pipe on stdin
--max-turns 8      cap on loop iterations
--raw              echo request/response bodies to stderr
```

## What it doesn't do

- No multi-tool orchestration, no parallel dispatch. One function, called zero or more
  times, sequentially.
- No streaming. Each hop is await-then-print.
- No persistence. The loop lives and dies in one run.
- The tool is filesystem-only (read). No write/execute side effects.
- No path sandbox. Whatever path the model returns is passed straight to
  `File.ReadAllText`. Deliberate: the day is about the loop, and the unbounded tool is
  what makes "the model picks the arguments" concrete.

## Expected results

- A question about the bundled `data/note.txt` → model calls `read_file`, answers from the
  file's real contents.
- A question answerable from knowledge (e.g. "what is 2+2") → model answers directly with
  no tool call.
- A wrong path → clean tool error, model asks rather than fabricating.
