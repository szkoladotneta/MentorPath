#!/usr/bin/env python3
import argparse
import sys


def validate(state, readiness_status, readiness_body):
    errors = []
    if state != "Running":
        errors.append("The app must be Running before maintenance.")

    if readiness_status != "200" or readiness_body.strip() != "Healthy":
        errors.append("The running app did not pass /health/ready with its configured certificate and key ring.")
    return errors


def main():
    parser = argparse.ArgumentParser(description="Gate Azure maintenance on live Data Protection readiness without reading app settings.")
    parser.add_argument("--state", required=True)
    parser.add_argument("--readiness-status", required=True)
    parser.add_argument("--readiness-body", required=True)
    arguments = parser.parse_args()

    errors = validate(arguments.state, arguments.readiness_status, arguments.readiness_body)
    if errors:
        for error in errors:
            print(f"::error::Data Protection preflight: {error}", file=sys.stderr)
        return 1

    print("The live Data Protection runtime readiness gate passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
