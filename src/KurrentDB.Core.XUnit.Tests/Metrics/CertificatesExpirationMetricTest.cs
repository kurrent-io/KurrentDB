// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Amazon.SQS.Model;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;
using Xunit;


namespace KurrentDB.Core.XUnit.Tests.Metrics;

public class CertificatesMetricTests : IDisposable {
	private readonly TestMeterListener<double> _doubleListener;

	private readonly Meter _meter;
	private readonly DateTimeOffset _now;

	public CertificatesMetricTests() {
		_meter = new Meter($"{typeof(CertificatesMetricTests)}");
		_doubleListener = new TestMeterListener<double>(_meter);
		_doubleListener.Observe();
		_now = DateTimeOffset.FromUnixTimeSeconds(Clock.Instance.SecondsSinceEpoch);
	}

	public void Dispose() => _doubleListener.Dispose();

	[Fact]
	public void When_Expired() {


		var b = new CertificateProviderTest(_now.AddDays(-10), _now.AddDays(-9));
		var sut = new CertificatesMetric(_meter, "cert_apocalypse", b);


		sut.Measure();
		var measurements = _doubleListener.RetrieveMeasurements("cert_apocalypse-day");

		Assert.Contains(
			measurements,
			m => {

				Assert.True(Math.Abs(m.Value + 9) < 0.01);


				Assert.Equal(CertificatesMetric.Tags.All, m.Tags.Select(t => t.Key));
				Assert.Contains(
					m.Tags.Single(t => t.Key == CertificatesMetric.Tags.ChainLevel).Value,
					CertificatesMetric.ChainLevels.All
				);
				Assert.Equal(
					CertificatesMetric.ValidityPeriods.Expired,
					m.Tags.Single(t => t.Key == CertificatesMetric.Tags.ValidityPeriod).Value);


				return true;
			});
	}
	[Fact]
	public void When_Ok() {


		var b = new CertificateProviderTest(_now.AddDays(-10), _now.AddDays(10));
		var sut = new CertificatesMetric(_meter, "cert_will_be_apocalypse", b);


		sut.Measure();
		var measurements = _doubleListener.RetrieveMeasurements("cert_will_be_apocalypse-day");

		Assert.Contains(
			measurements,
			m => {

				Assert.True(Math.Abs(m.Value -10)< 0.01);


				Assert.Equal(CertificatesMetric.Tags.All, m.Tags.Select(t => t.Key));
				Assert.Contains(
					m.Tags.Single(t => t.Key == CertificatesMetric.Tags.ChainLevel).Value,
					CertificatesMetric.ChainLevels.All
				);
				Assert.Equal(
					CertificatesMetric.ValidityPeriods.InPeriod,
					m.Tags.Single(t => t.Key == CertificatesMetric.Tags.ValidityPeriod).Value);

				return true;
			});
	}

	[Fact]
	public void When_Not_Yet_Valid() {


		var b = new CertificateProviderTest(_now.AddDays(10), _now.AddDays(11));
		var sut = new CertificatesMetric(_meter, "cert_future_apocalypse", b);


		sut.Measure();
		var measurements = _doubleListener.RetrieveMeasurements("cert_future_apocalypse-day");

		Assert.Contains(
			measurements,
			m => {

				 Assert.True(Math.Abs(m.Value -11 ) < 0.01);

				Assert.Equal(CertificatesMetric.Tags.All, m.Tags.Select(t => t.Key));
				Assert.Contains(
					m.Tags.Single(t => t.Key == CertificatesMetric.Tags.ChainLevel).Value,
					CertificatesMetric.ChainLevels.All
				);
				Assert.Equal(
					CertificatesMetric.ValidityPeriods.NotValidYet,
					m.Tags.Single(t => t.Key == CertificatesMetric.Tags.ValidityPeriod).Value);
				return true;
			});
	}


	public class CertificateProviderTest : CertificateProvider {


		public CertificateProviderTest(DateTimeOffset notBefore, DateTimeOffset notAfter) {
			using RSA rsa = RSA.Create();
			var request = new CertificateRequest("CN=SelfSignedCert", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);


			Certificate=request.CreateSelfSigned(notBefore, notAfter);
			IntermediateCerts = new X509Certificate2Collection(

				request.CreateSelfSigned(notBefore, notAfter)
				);
			TrustedRootCerts = new X509Certificate2Collection(request.CreateSelfSigned(notBefore, notAfter));
		}

		public override LoadCertificateResult LoadCertificates(ClusterVNodeOptions options) =>
			throw new UnsupportedOperationException("");

		public override string GetReservedNodeCommonName() =>
			throw new UnsupportedOperationException("");
	}

}
