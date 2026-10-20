# Search providers

A provider lets any program answer in Bloom's search line. Put `providers.json` in Bloom's data folder (`%APPDATA%\Bloom`). The file is read again when it changes.

```json
[
  {
    "keyword": "ff",
    "name": "Find files",
    "command": ["powershell", "-NoProfile", "-File", "C:\\Tools\\find-files.ps1"],
    "cwd": "C:\\Tools",
    "minChars": 2,
    "timeoutMs": 8000
  }
]
```

| Field | Meaning |
|---|---|
| `keyword` | One word, no spaces, matched without case. `ff report` calls the provider; `ff` alone offers it. |
| `name` | Shown on the rows. Defaults to the keyword. |
| `command` | The program and its arguments, as an array. No shell reads it. |
| `cwd` | Working folder. Optional. |
| `minChars` | Shortest search words (default 1). |
| `timeoutMs` | Time limit, 200 to 120000 (default 8000). |

A line that starts with the keyword and a space calls the command with the rest of the line as one more, last argument. Bloom waits 250 ms after the last key, ends the previous run when the words change, and runs the command in its own job, so anything it started ends with it. The command prints one JSON object to stdout (UTF-8):

```json
{
  "items": [
    { "title": "report.docx", "subtitle": "C:\\Docs", "action": { "type": "run", "value": ["explorer.exe", "/select,C:\\Docs\\report.docx"] } },
    { "title": "Copy the id", "action": { "type": "copy", "value": "A-1042" } },
    { "title": "Open the page", "action": { "type": "url", "value": "https://example.com/a-1042" } }
  ],
  "error": "optional one-line message"
}
```

- `items`: up to 20 rows. A row needs a `title`; `subtitle` and `action` are optional. A row without an action is shown only.
- `action` runs on Enter:
  - `url`: opens an `http` or `https` address. Other schemes are refused.
  - `copy`: copies the text.
  - `run`: starts the program from an array (`["program", "arg", …]`). No shell reads it.
- `error`: shown as a line under the rows. Output that is not this object shows one error line.

A provider's keyword wins over a quicklink or command with the same word.

`providers/find-files.ps1` is a working example (files under Documents whose name holds the words; Enter selects the file in Explorer).
