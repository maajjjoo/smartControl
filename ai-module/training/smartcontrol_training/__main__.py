"""Línea de comandos de la herramienta de entrenamiento (ejecutar desde ai-module/training).

    python -m smartcontrol_training evaluate --data ../data --kind logistic --report ../out/report.json
    python -m smartcontrol_training train    --data ../data --kind mlp --version 2026-11-01 --out ../out/model.json
    python -m smartcontrol_training synthetic --out <carpeta temporal>

Con grabaciones sintéticas hay que añadir --allow-synthetic; el resultado se marca como sintético.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from smartcontrol_training import evaluate, export, model, synthetic
from smartcontrol_training.recording import RecordingError, load_dataset


def _paths(values: list[str]) -> list[Path]:
    paths = []
    for value in values:
        path = Path(value)
        paths += sorted(path.glob("*.jsonl")) if path.is_dir() else [path]
    return paths


def _trainer(kind: str, seed: int):
    if kind == "logistic":
        return lambda x, y: model.train_logistic(x, y, seed=seed)
    return lambda x, y: model.train_mlp(x, y, seed=seed)


def _warn_if_synthetic(source: str) -> None:
    if source != "physical-watch":
        print(evaluate.SYNTHETIC_WARNING, file=sys.stderr)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(prog="smartcontrol_training")
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("train", "evaluate"):
        p = sub.add_parser(name)
        p.add_argument("--data", nargs="+", required=True, help="archivos .jsonl o carpetas")
        p.add_argument("--kind", choices=("logistic", "mlp"), default="logistic")
        p.add_argument("--threshold", type=float, default=0.80)
        p.add_argument("--seed", type=int, default=0)
        p.add_argument("--allow-synthetic", action="store_true", help="solo para probar el código")
    sub.choices["train"].add_argument("--out", type=Path, required=True)
    sub.choices["train"].add_argument("--version", required=True)
    sub.choices["evaluate"].add_argument("--report", type=Path)
    gen = sub.add_parser("synthetic")
    gen.add_argument("--out", type=Path, required=True)
    gen.add_argument("--participants", type=int, default=3)
    gen.add_argument("--per-class", type=int, default=20)
    gen.add_argument("--seed", type=int, default=0)
    args = parser.parse_args(argv)

    try:
        if args.command == "synthetic":
            for path in synthetic.generate(args.out, args.participants, args.per_class, args.seed):
                print(f"escrito (SINTÉTICO) {path}")
            return 0

        dataset = load_dataset(_paths(args.data), allow_synthetic=args.allow_synthetic)
        _warn_if_synthetic(dataset.data_source)
        if args.command == "evaluate":
            result = evaluate.report(dataset, _trainer(args.kind, args.seed), args.threshold)
            text = json.dumps(result, indent=2, ensure_ascii=False)
            if args.report:
                args.report.parent.mkdir(parents=True, exist_ok=True)
                args.report.write_bytes((text + "\n").encode("utf-8"))
            print(text)
            return 0

        samples = evaluate.samples_from(dataset)
        trained = _trainer(args.kind, args.seed)(samples.features, samples.labels)
        model_json = export.to_json(trained, dataset.data_source, len(dataset.participants), len(samples.labels),
                                    dataset.sha256, args.version, args.threshold)
        path = export.write_model(args.out, model_json)
        print(f"modelo {model_json['dataSource']} escrito en {path} (y {path.name}.sha256)")
        return 0
    except (RecordingError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
