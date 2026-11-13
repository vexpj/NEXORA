"""Проверка репозитория на случайно закоммиченные секреты. Запуск: python build/check_secrets.py
Выходит с кодом 1, если что-то найдено (сборка в GitHub Actions при этом останавливается)."""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

BAD_FILE = re.compile(r"(\.pfx|\.p12|\.pem|\.key|\.snk|\.cer)$|(^|/)\.env(\..+)?$|(^|/)(settings|account|profiles|secrets)\.json$", re.I)

PATTERNS = {
    "приватный ключ": r"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----",
    "токен GitHub": r"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b|\bgithub_pat_[A-Za-z0-9_]{22,}\b",
    "ключ CurseForge (bcrypt-вид)": r"\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53}",
    "секрет приложения Azure": r"\b[A-Za-z0-9_~.-]{3}8Q~[A-Za-z0-9_~.-]{30,}",
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
        print("НАЙДЕНЫ ВОЗМОЖНЫЕ СЕКРЕТЫ:")
        for p in problems:
            print("  - " + p)
        print("\nУдалите их из кода и истории Git и СРАЗУ перевыпустите (отзовите) скомпрометированный ключ.")
        return 1
    print("Секретов не найдено.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
