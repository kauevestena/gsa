#!/usr/bin/env python3
"""Portable entry point for the M0 build, test, content and export pipeline."""
from check_bootstrap import main

if __name__ == "__main__":
    raise SystemExit(main())
