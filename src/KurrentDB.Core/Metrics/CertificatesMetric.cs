// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Security.Cryptography.X509Certificates;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Time;

namespace KurrentDB.Core.Metrics;

public class CertificatesMetric {
	private readonly Meter _meter;
	private readonly string _name;
	private readonly CertificateProvider _certificateProvider;
	private readonly List<CertificateExpiration> _subMetric= [];
	private readonly IClock _clock;

	public static class ChainLevels {
		public const string Node="node";
		public const string Intermediate = "intermediate";
		public const string Root = "root";
		public static readonly string[] All = [Node, Intermediate, Root];
	}
	public static class Tags {
		public const string ChainLevel = "chain_level";
		public const string SerialNumber = "serial_number";
		public const string Thumbprint = "thumbprint";
		public const string ValidityPeriod = "validity_period";
		public static readonly string[] All = [ChainLevel, SerialNumber, Thumbprint, ValidityPeriod];
	}
	public static class ValidityPeriods {
		public const string InPeriod="0";
		public const string NotValidYet = "1";
		public const string Expired = "-1";
	}



	public CertificatesMetric(Meter meter, string name,
		CertificateProvider certificateProvider,
		IClock clock = null) {
		_meter = meter;
		_name = name;
		_certificateProvider = certificateProvider;
		_clock = clock ?? Clock.Instance;

		MeasureAllCerts(meter, name, certificateProvider);
	}

	public void Measure() {
		foreach (var subMetric in _subMetric)
			subMetric.Measure();
	}

	public void Renewed() => MeasureAllCerts(_meter, _name, _certificateProvider);

	private void MeasureAllCerts(Meter meter, string name, CertificateProvider certificateProvider)
	{
		_subMetric.Clear();
		_subMetric.Add(new CertificateExpiration(certificateProvider.Certificate, ChainLevels.Node, meter, name,_clock));
		foreach (var intermediate in certificateProvider.IntermediateCerts)
			_subMetric.Add(new CertificateExpiration(intermediate, ChainLevels.Intermediate, meter, name,_clock));

		foreach (var root in certificateProvider.TrustedRootCerts)
			_subMetric.Add(new CertificateExpiration(root, ChainLevels.Root, meter, name,_clock));
	}


	public class CertificateExpiration(X509Certificate2 cert, string type, Meter meter, string name, IClock clock ) {

		private readonly Gauge<double> _gauge = meter.CreateGauge<double>(name, "day", "Days before the certificate expires");

		private readonly KeyValuePair<string, object>[] _tags = [
			new(Tags.ChainLevel, type),
			new(Tags.SerialNumber, cert.GetSerialNumberString()),
			new(Tags.Thumbprint, cert.Thumbprint),
			new(Tags.ValidityPeriod, ValidityPeriod(DateTimeOffset.FromUnixTimeSeconds(clock.SecondsSinceEpoch).DateTime, cert.NotBefore, cert.NotAfter).ToString())


		];

		private static string ValidityPeriod(DateTime now, DateTime notBefore, DateTime notAfter) =>
			 notBefore <= now && now <= notAfter
				? ValidityPeriods.InPeriod
				: now <= notBefore
					? ValidityPeriods.NotValidYet
					: ValidityPeriods.Expired;

		public void Measure() {
			var certApocalypseIn = (cert.NotAfter - DateTime.Now).TotalDays;
			_gauge.Record(certApocalypseIn, _tags);

		}
	}
}
