#!/usr/bin/env python3
"""Tests for multi-question suite loading and structured scoring."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

HARNESS_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HARNESS_DIR / "lib"))

from read_prompts import load_question_manifest
from score import score_answer_text, score_run_directory


ORDER_STATUS_GROUND_TRUTH = {
    "id": "order_status_completion",
    "rubric": "structured_terms",
    "checks": [
        {
            "name": "paid_date_and_completion_gate",
            "weight": 25,
            "required_terms": [
                "PaymentStatus.Paid",
                "PaidDateUtc",
                "ShippingStatus.ShippingNotRequired",
                "CompleteOrderWhenDelivered",
                "ShippingStatus.Delivered",
                "ShippingStatus.Shipped",
            ],
            "ordered_terms": [
                "PaymentStatus.Paid",
                "PaidDateUtc",
                "ShippingStatus.ShippingNotRequired",
                "CompleteOrderWhenDelivered",
            ],
        },
        {
            "name": "pending_processing_transitions",
            "weight": 25,
            "required_terms": [
                "OrderStatus.Pending",
                "PaymentStatus.Authorized",
                "PaymentStatus.Paid",
                "ShippingStatus.PartiallyShipped",
                "OrderStatus.Processing",
            ],
        },
        {
            "name": "terminal_status_return",
            "weight": 20,
            "required_terms": [
                "OrderStatus.Cancelled",
                "OrderStatus.Complete",
                "UpdateOrderAsync",
                "return",
            ],
        },
        {
            "name": "final_complete_save",
            "weight": 30,
            "required_terms": [
                "SetOrderStatusAsync",
                "OrderStatus.Complete",
                "needOrderSave",
                "UpdateOrderAsync",
            ],
        },
    ],
}


class SuiteScoringTests(unittest.TestCase):
    def test_load_question_manifest_resolves_ground_truth_paths(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            ground_truth = root / "ground-truth" / "order-status.json"
            ground_truth.parent.mkdir()
            ground_truth.write_text(json.dumps(ORDER_STATUS_GROUND_TRUTH), encoding="utf-8")
            manifest = root / "prompts" / "questions.json"
            manifest.parent.mkdir()
            manifest.write_text(
                json.dumps(
                    [
                        {
                            "id": "order_status_completion",
                            "prompt": "Explain order status completion.",
                            "ground_truth": "../ground-truth/order-status.json",
                        }
                    ]
                ),
                encoding="utf-8",
            )

            questions = load_question_manifest(manifest)

            self.assertEqual(questions[0]["id"], "order_status_completion")
            self.assertEqual(questions[0]["prompt"], "Explain order status completion.")
            self.assertEqual(questions[0]["ground_truth_path"], str(ground_truth.resolve()))

    def test_structured_scoring_rewards_complete_answer(self) -> None:
        answer = """
        CheckAndSaveOrderStatusAsync first reacts to PaymentStatus.Paid and sets PaidDateUtc.
        Paid orders complete immediately for ShippingStatus.ShippingNotRequired. When shipping
        is required, CompleteOrderWhenDelivered means only ShippingStatus.Delivered completes;
        otherwise ShippingStatus.Shipped or ShippingStatus.Delivered completes. For
        OrderStatus.Pending, PaymentStatus.Authorized or PaymentStatus.Paid moves to
        OrderStatus.Processing, as do ShippingStatus.PartiallyShipped, Shipped, and Delivered.
        OrderStatus.Cancelled and OrderStatus.Complete update through UpdateOrderAsync if needed
        and return. If completed is true, SetOrderStatusAsync sets OrderStatus.Complete.
        needOrderSave controls whether UpdateOrderAsync is still needed.
        """

        score = score_answer_text(answer, ground_truth=ORDER_STATUS_GROUND_TRUTH)

        self.assertEqual(score.total_score, 100)

    def test_structured_scoring_rejects_plausible_incomplete_answer(self) -> None:
        answer = """
        Orders become Complete when they are paid and shipped. Pending paid orders move to
        Processing, and cancelled orders are ignored.
        """

        score = score_answer_text(answer, ground_truth=ORDER_STATUS_GROUND_TRUTH)

        self.assertLess(score.total_score, 50)

    def test_structured_scoring_rejects_shuffled_flow_terms(self) -> None:
        answer = """
        CompleteOrderWhenDelivered ShippingStatus.ShippingNotRequired PaidDateUtc
        PaymentStatus.Paid ShippingStatus.Delivered ShippingStatus.Shipped.
        OrderStatus.Pending PaymentStatus.Authorized PaymentStatus.Paid
        ShippingStatus.PartiallyShipped OrderStatus.Processing OrderStatus.Cancelled
        OrderStatus.Complete UpdateOrderAsync return SetOrderStatusAsync needOrderSave.
        """

        score = score_answer_text(answer, ground_truth=ORDER_STATUS_GROUND_TRUTH)

        failed_checks = {check.name for check in score.checks if not check.passed}
        self.assertIn("paid_date_and_completion_gate", failed_checks)

    def test_score_run_directory_scores_each_question_and_aggregate(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = Path(tmp)
            tool_dir = run_dir / "cursor"
            tool_dir.mkdir()
            (tool_dir / "baseline_order_status_completion.txt").write_text(
                "PaymentStatus.Paid PaidDateUtc ShippingStatus.ShippingNotRequired "
                "CompleteOrderWhenDelivered ShippingStatus.Delivered ShippingStatus.Shipped "
                "OrderStatus.Pending PaymentStatus.Authorized PaymentStatus.Paid "
                "ShippingStatus.PartiallyShipped OrderStatus.Processing OrderStatus.Cancelled "
                "OrderStatus.Complete UpdateOrderAsync return SetOrderStatusAsync needOrderSave",
                encoding="utf-8",
            )
            (tool_dir / "test_order_status_completion.txt").write_text(
                "Orders become Complete when paid and shipped.",
                encoding="utf-8",
            )
            ground_truth = run_dir / "order-status.json"
            ground_truth.write_text(json.dumps(ORDER_STATUS_GROUND_TRUTH), encoding="utf-8")
            questions = [
                {
                    "id": "order_status_completion",
                    "prompt": "Explain order status completion.",
                    "ground_truth_path": str(ground_truth),
                }
            ]

            results = score_run_directory(run_dir, questions=questions)

            tool_scores = results["tools"]["cursor"]
            self.assertIn("questions", tool_scores)
            self.assertGreater(tool_scores["aggregate"]["degradation_delta"], 0)
            self.assertLess(
                tool_scores["questions"]["order_status_completion"]["test"]["total_score"],
                tool_scores["questions"]["order_status_completion"]["baseline"]["total_score"],
            )

    def test_score_run_directory_uses_manifest_recorded_in_run(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            run_dir = root / "run"
            run_dir.mkdir()
            tool_dir = run_dir / "cursor"
            tool_dir.mkdir()
            ground_truth = root / "custom-ground-truth.json"
            ground_truth.write_text(json.dumps(ORDER_STATUS_GROUND_TRUTH), encoding="utf-8")
            manifest = root / "custom-questions.json"
            manifest.write_text(
                json.dumps(
                    [
                        {
                            "id": "order_status_completion",
                            "prompt": "Explain order status completion.",
                            "ground_truth": "custom-ground-truth.json",
                        }
                    ]
                ),
                encoding="utf-8",
            )
            (run_dir / "manifest.json").write_text(
                json.dumps({"config": {"question_manifest": str(manifest)}}),
                encoding="utf-8",
            )
            (tool_dir / "baseline_order_status_completion.txt").write_text(
                "PaymentStatus.Paid PaidDateUtc ShippingStatus.ShippingNotRequired "
                "CompleteOrderWhenDelivered ShippingStatus.Delivered ShippingStatus.Shipped",
                encoding="utf-8",
            )

            results = score_run_directory(run_dir)

            self.assertEqual(results["questions"][0]["id"], "order_status_completion")


if __name__ == "__main__":
    unittest.main()
