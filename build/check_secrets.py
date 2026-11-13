import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

BAD_FILE = re.compile(
    r"(?:^|/)(?:.*(?:secret|token|key|credential|auth|password|cert|pfx|pem|key|private).*|.*(?:\.pem|\.pfx|\.key|\.jks|\.keystore|\.p12)$)",
    re.IGNORECASE,
)

PATTERNS = {
    "JWT-токен": r"\beyJ[A-Za-z0-9_-]{15,}\.eyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}",
    "токен Microsoft (refresh)": r"\bM\.C\d{3}_[A-Za-z0-9._!*$-]{40,}",
    "ключ AWS": r"\bAKIA[0-9A-Z]{16}\b",
    "ключ Google": r"\bAIza[0-9A-Za-z_-]{35}\b",
    "токен Slack/Discord": r"\bxox[baprs]-[A-Za-z0-9-]{10,}|[MNO][A-Za-z\d]{23,25}\.[\w-]{6}\.[\w-]{27,}",
    "присвоение секрета строкой": r"(?i)\b(password|passwd|pwd|secret|client_secret|access_token|refresh_token|api[_-]?key)\b\s*[:=]\s*[\"'][^\"'\s]{12,}[\"']",
}
TEXT_EXT = {".cs", ".xaml", ".json", ".yml", ".yaml", ".ps1", ".py", ".iss", ".props", ".csproj", ".md", ".txt", ".config", ".xml", ".sln", ".manifest"}


def tracked_files():
    try:
        out = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
        return [ROOT / p for p in out.splitlines() if p]
    except (OSError, subprocess.CalledProcessError):
        return [p for p in ROOT.rglob("*") if p.is_file() and ".git" not in p.parts and "bin" not in p.parts and "obj" not in p.parts]


def safe_print(message: str) -> None:
    try:
        print(message)
    except UnicodeEncodeError:
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
            print(message)
        except (AttributeError, ValueError):
            print(message.encode("utf-8", errors="replace").decode("utf-8", errors="replace"))


def main() -> int:
    problems = []
    for f in tracked_files():
        rel = f.relative_to(ROOT).as_posix()
        if rel == "build/check_secrets.py":
            continue
        if BAD_FILE.search(rel):
            problems.append(f"{rel}: файл с секретами или личными данными не должен лежать в репозитории")
            continue
        if f.suffix.lower() not in TEXT_EXT or not f.exists():
            continue
        try:
            text = f.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        for name, rx in PATTERNS.items():
            for m in re.finditer(rx, text):
                line = text.count("\n", 0, m.start()) + 1
                problems.append(f"{rel}:{line}: похоже на «{name}»")
    if problems:
        safe_print("НАЙДЕНЫ ВОЗМОЖНЫЕ СЕКРЕТЫ:")
        for p in problems:
            safe_print("  - " + p)
        safe_print("\nУдалите их из кода и истории Git и СРАЗУ перевыпустите (отзовите) скомпрометированный ключ.")
        return 1
    safe_print("Секретов не найдено.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
